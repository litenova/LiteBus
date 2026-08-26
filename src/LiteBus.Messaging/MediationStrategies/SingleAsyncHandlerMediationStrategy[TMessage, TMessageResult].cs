using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
///     1. Executes pre-handlers, stopping early when one short-circuits.
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
                    messageResult = CoerceShortCircuitResult(directive);
                    return messageResult;
                }

                var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;

                if (handler is null)
                {
                    throw new LiteBusConfigurationException(
                        $"Handler for {typeof(TMessage).Name} is not of the expected type.");
                }

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
            outcome = MessageOutcome.Canceled;
            failure = canceledException;
            throw;
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            outcome = MessageOutcome.Failed;
            failure = e;

            MessageErrorContext errorContext;

            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                errorContext = await messageDependencies.RunAsyncErrorHandlers(
                    message,
                    messageResult,
                    ExceptionDispatchInfo.Capture(e),
                    executionContext.CancellationToken).ConfigureAwait(false);
            }

            if (errorContext.HandledResult is TMessageResult handledResult)
            {
                return handledResult;
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

        return messageResult!;
    }

    /// <summary>
    ///     Converts a short-circuit directive's result to the message result type.
    /// </summary>
    /// <param name="directive">The short-circuiting directive returned by a pre-handler.</param>
    /// <returns>The result the caller receives.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when the message has a result type and the directive supplied no value, or supplied one of the wrong
    ///     type.
    /// </exception>
    private static TMessageResult CoerceShortCircuitResult(PipelineDirective directive)
    {
        if (directive.Result is TMessageResult typedResult)
        {
            return typedResult;
        }

        if (directive.Result is null)
        {
            throw new LiteBusConfigurationException(
                $"A short-circuiting pre-handler for '{typeof(TMessage).Name}' must supply a result of type "
                + $"'{typeof(TMessageResult).Name}', because the message has a result type. "
                + "Pass it to PipelineDirective.ShortCircuit(result).");
        }

        throw new LiteBusConfigurationException(
            $"A short-circuiting pre-handler for '{typeof(TMessage).Name}' supplied a result of type "
            + $"'{directive.Result.GetType().Name}', but the message expects '{typeof(TMessageResult).Name}'.");
    }
}
