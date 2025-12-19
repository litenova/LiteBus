using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

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
public sealed class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult> : IMessageMediationStrategy<TMessage, Task<TMessageResult>> where TMessage : notnull
{
    public async Task<TMessageResult> Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);

        if (messageDependencies.MainHandlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage), messageDependencies.MainHandlers.Count);
        }
        
        messageDependencies.DiagnosticHandlers.OnMediationStarting(message, executionContext);

        TMessageResult? messageResult = default; // Nullable within the method

        try
        {
            await messageDependencies.RunAsyncPreHandlers(message);

            var handler = messageDependencies.MainHandlers.Single().Handler.Value;

            if (handler is null)
            {
                throw new InvalidOperationException($"Handler for {typeof(TMessage).Name} is not of the expected type.");
            }

            messageResult = await (Task<TMessageResult>) handler.Handle(message);

            await messageDependencies.RunAsyncPostHandlers(message, messageResult);
            
            messageDependencies.DiagnosticHandlers.OnMediationCompleted(message, messageResult ,executionContext);
        }
        catch (LiteBusExecutionAbortedException)
        {
            if (executionContext.MessageResult is null)
            {
                throw new InvalidOperationException(
                    $"A Message result of type '{typeof(TMessageResult).Name}' is required when the execution is aborted as this message has a specific result.");
            }

            return await Task.FromResult((TMessageResult) executionContext.MessageResult);
        }
        catch (Exception e) when (e is not LiteBusExecutionAbortedException)
        {
            messageDependencies.DiagnosticHandlers.OnMediationFaulted(message, e, executionContext);
            await messageDependencies.RunAsyncErrorHandlers(message, messageResult, ExceptionDispatchInfo.Capture(e));
        }

        // If we get here, messageResult should be non-null because:
        // 1. The handler assigned a value, or
        // 2. An exception was thrown and we didn't reach this point
        return messageResult!;
    }
}