using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Represents a mediation strategy that processes a message through a single asynchronous handler.
/// </summary>
/// <typeparam name="TMessage">The type of message being mediated.</typeparam>
/// <remarks>
///     This strategy ensures that only one handler is registered for the message type and then:
///     1. Executes pre-handlers, stopping early when one short-circuits.
///     2. Delegates the message processing to the registered handler.
///     3. Executes post-handlers, unless the pipeline suppressed them.
///     4. Routes exceptions to registered error handlers.
///     5. Reports the outcome to completion handlers, on every path.
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
    ///     When a pre-handler short-circuits, the mediation ends with <see cref="MessageOutcome.Aborted" /> and the
    ///     main handler never runs.
    /// </remarks>
    public async Task Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        object? messageResult = null;
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = MessageOutcome.Succeeded;
        Exception? failure = null;
        string? abortReason = null;

        try
        {
            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                var directive = await messageDependencies
                    .RunAsyncPreHandlers(message, executionContext.CancellationToken)
                    .ConfigureAwait(false);

                if (directive.IsShortCircuit)
                {
                    outcome = MessageOutcome.Aborted;
                    abortReason = directive.Reason;
                    return;
                }

                var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;

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
            outcome = MessageOutcome.Canceled;
            failure = canceledException;
            throw;
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            outcome = MessageOutcome.Failed;
            failure = e;

            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                await messageDependencies.RunAsyncErrorHandlers(
                    message,
                    messageResult,
                    ExceptionDispatchInfo.Capture(e),
                    executionContext.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await messageDependencies.RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    outcome,
                    executionContext.MessageResult ?? messageResult,
                    failure,
                    abortReason,
                    Stopwatch.GetElapsedTime(startedAt))
                .ConfigureAwait(false);
        }
    }
}
