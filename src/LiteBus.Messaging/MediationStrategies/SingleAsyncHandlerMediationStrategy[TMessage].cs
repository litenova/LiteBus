using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Pipeline;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Represents a mediation strategy that processes a message through a single asynchronous handler.
/// </summary>
/// <typeparam name="TMessage">The type of message being mediated.</typeparam>
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
public sealed class SingleAsyncHandlerMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
    where TMessage : notnull
{
    /// <summary>
    ///     Mediates a message by executing the appropriate handler and orchestrating the handling pipeline.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">
    ///     The dependencies required for message handling, including handlers, pre-handlers,
    ///     post-handlers, and error handlers.
    /// </param>
    /// <param name="executionContext">
    ///     The context in which the mediation is executed, providing access to cancellation tokens,
    ///     shared data, and other execution-related information.
    /// </param>
    /// <returns>A task representing the asynchronous mediation operation.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is found for the message type.</exception>
    /// <exception cref="MultipleHandlerFoundException">Thrown when more than one handler is found for the message type.</exception>
    /// <remarks>
    ///     The mediation process includes executing pre-handlers, the main handler, and post-handlers in sequence.
    ///     If an exception occurs during any stage, the appropriate error handlers are executed.
    ///     When a decision stops the pipeline, the mediation ends with <see cref="MediationOutcome.Answered" /> or
    ///     <see cref="MediationOutcome.Denied" /> and the main handler never runs.
    /// </remarks>
    public async Task Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        object? messageResult = null;
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
                        // A message that produces no result has nothing a refusal mapper could return, so a refusal
                        // always reaches the caller as an exception. It is excluded from the recoverable filter, so
                        // error handlers do not see a decision as a fault.
                        var refusal = decision.CreateRefusalException(message.GetType());
                        failure = refusal;
                        throw refusal;
                    }

                    return;
                }

                var handler = SingleMainHandlerResolver.Resolve(messageDependencies, message.GetType());

                await HandlerInvocation.InvokeMainHandlerAsync<TMessage>(
                    handler,
                    message,
                    executionContext.CancellationToken).ConfigureAwait(false);

                await messageDependencies.RunAsyncPostHandlers(
                    message,
                    messageResult,
                    executionContext.CancellationToken).ConfigureAwait(false);
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

                // A message that produces no result has nothing a refusal mapper could return, so a classified fault
                // always reaches the caller as the refusal exception. It is excluded from the recoverable filter, so
                // error handlers do not see a refusal as a fault.
                var refusalException = refusal.CreateException(message.GetType(), e);
                failure = refusalException;
                throw refusalException;
            }

            outcome = MediationOutcome.Failed;
            failure = e;

            await messageDependencies
                .RunAsyncErrorHandlers(message, messageResult, e, executionContext)
                .ConfigureAwait(false);

            // Reaching here means an error handler recovered: the stage rethrows otherwise. The outcome stays Failed
            // so the audit trail still records that a fault occurred, but no exception is recorded, which is what
            // distinguishes a recovered fault from one that propagated and what lets a completion-handler fault
            // propagate instead of being suppressed into an exception nobody receives.
            failure = null;
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
    }
}
