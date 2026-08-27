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
///     1. Executes the pre stages, stopping early when a guard refuses, a validator reports the message malformed,
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
        var ending = MediationEnding.Succeeded;

        try
        {
            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                var stop = await messageDependencies
                    .RunAsyncPreStages(message, executionContext.CancellationToken)
                    .ConfigureAwait(false);

                if (stop.StopsPipeline)
                {
                    ending = ending.Stopped(stop);

                    if (stop.IsRefusal)
                    {
                        // A refusal carries no result of its own, so the value comes from a registered mapper. Without
                        // one it reaches the caller as an exception, which is excluded from the recoverable filter so
                        // error handlers do not see a decision as a fault.
                        try
                        {
                            messageResult = messageDependencies
                                .ResolveRefusalResult<TMessageResult>(message, stop);
                        }
                        catch (Exception refusal) when (refusal is LiteBusMessageDeniedException
                                                            or LiteBusMessageInvalidException)
                        {
                            ending = ending.Refused(refusal);
                            throw;
                        }

                        return messageResult;
                    }

                    messageResult = stop.ResolveResult<TMessageResult>(message.GetType());
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
            ending = ending.Canceled(canceledException);
            throw;
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            ending = ending.Faulted(e);

            var errorContext = await messageDependencies
                .RunAsyncErrorHandlers(message, messageResult, e, executionContext)
                .ConfigureAwait(false);

            if (errorContext.HandledResult is TMessageResult handledResult)
            {
                return handledResult;
            }
        }
        finally
        {
            await messageDependencies
                .RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    ending,
                    messageResult,
                    Stopwatch.GetElapsedTime(startedAt))
                .ConfigureAwait(false);
        }

        return messageResult!;
    }
}
