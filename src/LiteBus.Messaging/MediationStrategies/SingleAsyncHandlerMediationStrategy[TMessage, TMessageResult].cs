using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Pipeline;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Represents a mediation strategy that processes a message through a single asynchronous handler.
/// </summary>
/// <typeparam name="TMessage">The type of message being mediated.</typeparam>
/// <typeparam name="TMessageResult">The type of the result produced by the handler.</typeparam>
/// <remarks>
///     This strategy ensures that only one handler is registered for the message type and then:
///     1. Executes the pre stages, stopping early when a guard denies, a validator reports the message malformed,
///     or a shortcut answers or denies.
///     2. Delegates the message processing to the registered handler.
///     3. Executes post-handlers, unless the pipeline suppressed them.
///     4. Offers a fault to the registered refusal classifiers before the error handlers; a recognized fault ends the
///     mediation as that refusal instead.
///     5. Routes unclassified exceptions to registered error handlers.
///     6. Reports the outcome to completion handlers, on every path.
///     A fault the recoverable filter excludes, such as a resolution failure or an idempotency misconfiguration,
///     bypasses the error handlers but is still recorded before completion runs, so completion never observes
///     <see cref="MediationOutcome.Succeeded" /> for a mediation that threw. When error handlers recover, the outcome
///     stays <see cref="MediationOutcome.Failed" /> but no exception is recorded, which is what distinguishes a
///     recovered fault from one that propagated.
/// </remarks>
public sealed class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult>
    : IMessageMediationStrategy<TMessage, Task<TMessageResult>>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TMessageResult> Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        TMessageResult? messageResult = default;
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = MediationTelemetry.StartMediation(message.GetType());
        var outcome = MediationOutcome.Succeeded;
        Exception? failure = null;
        string? reason = null;
        string? code = null;

        try
        {
            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                var decision = await messageDependencies
                    .RunAsyncPreStages(message, executionContext.CancellationToken)
                    .ConfigureAwait(false);

                if (decision.StopsPipeline)
                {
                    outcome = decision.Outcome;
                    reason = decision.Reason;
                    code = decision.Code;

                    // Only a Try call installs a capture, so this is a dictionary miss on every ordinary mediation.
                    MediationEndingCapture.Record(executionContext, decision);

                    if (decision.IsRefusal)
                    {
                        // A refusal carries no result of its own, so the value comes from a registered mapper. Without
                        // one it reaches the caller as an exception, which is excluded from the recoverable filter so
                        // error handlers do not see a decision as a fault.
                        try
                        {
                            messageResult = messageDependencies
                                .ResolveRefusalResult<TMessageResult>(message, decision);
                        }
                        catch (Exception refusal) when (refusal is LiteBusMessageDeniedException
                                                            or LiteBusMessageInvalidException)
                        {
                            failure = refusal;
                            throw;
                        }

                        return messageResult;
                    }

                    messageResult = decision.ResolveResult<TMessageResult>(message.GetType());
                    return messageResult;
                }

                var handler = SingleMainHandlerResolver.Resolve(messageDependencies, message.GetType());

                messageResult = await HandlerInvocation.InvokeMainHandlerAsync<TMessage, TMessageResult>(
                    handler,
                    message,
                    executionContext.CancellationToken).ConfigureAwait(false);

                await messageDependencies.RunAsyncPostHandlers(
                    message,
                    messageResult,
                    executionContext.CancellationToken).ConfigureAwait(false);
            }

            // A post-handler may have written an override result to the execution context.
            // When present, it takes precedence over the value returned by the main handler.
            if (executionContext.MessageResult is not null)
            {
                return (TMessageResult) executionContext.MessageResult;
            }
        }
        catch (OperationCanceledException canceledException)
        {
            outcome = MediationOutcome.Canceled;
            failure = canceledException;
            throw;
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            if (MediationExceptionFilters.TryClassifyRefusal(
                    messageDependencies.FaultRefusalClassifiers,
                    e,
                    out var refusal))
            {
                outcome = refusal.Outcome;
                reason = refusal.Reason;
                code = refusal.Code;

                // Only a Try call installs a capture, so this is a dictionary miss on every ordinary mediation.
                MediationEndingCapture.Record(executionContext, refusal);

                // A classified fault is a refusal rather than a fault, so the error handlers never see it and the
                // completion stage observes the refusal outcome. Without a mapper it reaches the caller as the
                // refusal exception, which is excluded from the recoverable filter so it is not offered again.
                try
                {
                    messageResult = messageDependencies
                        .ResolveRefusalResult<TMessageResult>(message, refusal, e);
                }
                catch (Exception refusalException) when (refusalException is LiteBusMessageDeniedException
                                                             or LiteBusMessageInvalidException)
                {
                    failure = refusalException;
                    throw;
                }

                return messageResult;
            }

            outcome = MediationOutcome.Failed;
            failure = e;

            var errorContext = await messageDependencies
                .RunAsyncErrorHandlers(message, messageResult, e, executionContext)
                .ConfigureAwait(false);

            // Reaching here means an error handler recovered: the stage rethrows otherwise. The outcome stays Failed
            // so the audit trail still records that a fault occurred, but no exception is recorded, which is what
            // distinguishes a recovered fault from one that propagated and what lets a completion-handler fault
            // propagate instead of being suppressed into an exception nobody receives.
            failure = null;

            if (errorContext.HandledResult is TMessageResult handledResult)
            {
                // The recovered value is what the caller receives, so the completion stage has to see it too. Returning
                // it without recording it would hand an audit trail the default the main handler never produced.
                messageResult = handledResult;

                return handledResult;
            }

            // A handler that claims the fault without a usable result recovers the message, and the caller receives the
            // default value. A global error handler cannot know a message's result type, so this is how it absorbs a
            // fault; a handler that has a fallback value supplies it through HandledResult.
        }
#pragma warning disable CA1031 // A non-recoverable fault must still be recorded before completion handlers run.
        catch (Exception e)
#pragma warning restore CA1031
        {
            // Resolution failures, idempotency misconfiguration, and application-thrown refusals bypass the error
            // stage, but completion must not observe Succeeded for a mediation that threw. An ending already
            // recorded, such as a refusal the pre stages decided, keeps what it recorded: only a mediation with no
            // recorded failure is marked Failed here.
            if (failure is null)
            {
                outcome = MediationOutcome.Failed;
                failure = e;
            }

            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            MediationTelemetry.RecordMediation(activity, message.GetType(), outcome, code, elapsed);

            await messageDependencies
                .RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    outcome,
                    failure,
                    reason,
                    code,
                    messageResult,
                    elapsed)
                .ConfigureAwait(false);
        }

        return messageResult!;
    }
}
