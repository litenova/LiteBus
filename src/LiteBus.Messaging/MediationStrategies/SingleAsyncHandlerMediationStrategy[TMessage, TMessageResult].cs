using System;
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
///     1. Executes pre-handlers.
///     2. Delegates the message processing to the registered handler.
///     3. Executes post-handlers.
///     In case of any exception during the process, it delegates the error handling to the registered error handlers.
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

        TMessageResult? messageResult = default;

        try
        {
            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                await messageDependencies.RunAsyncPreHandlers(message, executionContext.CancellationToken);

                var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;

                if (handler is null)
                {
                    throw new LiteBusConfigurationException(
                        $"Handler for {typeof(TMessage).Name} is not of the expected type.");
                }

                messageResult = await HandlerInvocation.InvokeMainHandlerAsync<TMessage, TMessageResult>(
                    handler,
                    message,
                    executionContext.CancellationToken);

                await messageDependencies.RunAsyncPostHandlers(
                    message,
                    messageResult,
                    executionContext.CancellationToken);
            }

            // A post-handler may have written an override result to the execution context.
            // When present, it takes precedence over the value returned by the main handler.
            if (executionContext.MessageResult is not null)
            {
                return (TMessageResult) executionContext.MessageResult;
            }
        }
        catch (LiteBusExecutionAbortedException)
        {
            if (executionContext.MessageResult is null)
            {
                throw new LiteBusConfigurationException(
                    $"A Message result of type '{typeof(TMessageResult).Name}' is required when the execution is aborted as this message has a specific result.");
            }

            return await Task.FromResult((TMessageResult) executionContext.MessageResult);
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            MessageErrorContext errorContext;

            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                errorContext = await messageDependencies.RunAsyncErrorHandlers(
                    message,
                    messageResult,
                    ExceptionDispatchInfo.Capture(e),
                    executionContext.CancellationToken);
            }

            if (errorContext.HandledResult is TMessageResult handledResult)
            {
                return handledResult;
            }
        }

        return messageResult!;
    }
}
