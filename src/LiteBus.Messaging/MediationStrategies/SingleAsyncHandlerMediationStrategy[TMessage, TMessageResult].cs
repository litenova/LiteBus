using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
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
///     or a shortcut answers.
///     2. Delegates the message processing to the registered handler.
///     3. Executes post-handlers, unless the pipeline suppressed them.
///     4. Routes exceptions to registered error handlers.
///     5. Reports the outcome to completion handlers, on every path.
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
        var outcome = MediationOutcome.Succeeded;
        Exception? failure = null;
        string? reason = null;

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

                var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;

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
            outcome = MediationOutcome.Failed;
            failure = e;

            var errorContext = await messageDependencies
                .RunAsyncErrorHandlers(message, messageResult, e, executionContext)
                .ConfigureAwait(false);

            if (errorContext.HandledResult is TMessageResult handledResult)
            {
                // The recovered value is what the caller receives, so the completion stage has to see it too. Returning
                // it without recording it would hand an audit trail the default the main handler never produced.
                messageResult = handledResult;

                return handledResult;
            }
        }
        finally
        {
            await messageDependencies
                .RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    outcome,
                    failure,
                    reason,
                    messageResult,
                    Stopwatch.GetElapsedTime(startedAt))
                .ConfigureAwait(false);
        }

        return messageResult!;
    }
}
