using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Represents a mediation strategy that processes a message through a single asynchronous handler.
/// </summary>
/// <typeparam name="TMessage">The type of message being mediated.</typeparam>
/// <remarks>
///     This strategy ensures that only one handler is registered for the message type and then:
///     1. Executes the pre stages, stopping early when a guard denies, a validator reports the message malformed,
///     or a shortcut answers.
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
                        // A message that produces no result has nothing a refusal mapper could return, so a refusal
                        // always reaches the caller as an exception. It is excluded from the recoverable filter, so
                        // error handlers do not see a decision as a fault.
                        var refusal = decision.CreateRefusalException(message.GetType());
                        failure = refusal;
                        throw refusal;
                    }

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
            outcome = MediationOutcome.Canceled;
            failure = canceledException;
            throw;
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            outcome = MediationOutcome.Failed;
            failure = e;

            await messageDependencies
                .RunAsyncErrorHandlers(message, messageResult, e, executionContext)
                .ConfigureAwait(false);
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
    }
}
