using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Invokes message pipeline handlers with an explicit cancellation token instead of relying on ambient context timing.
/// </summary>
/// <remarks>
///     A handler whose descriptor names exactly the strategy's message type is called through its typed contract directly.
///     Any other handler, one registered for a more derived runtime type than the strategy sees, is called through the closed
///     contract on its descriptor by <see cref="MainHandlerDispatch" />. Testing the handler instance against the strategy's
///     type instead would pick an arbitrary contract when one class implements several closed contracts that all fit.
/// </remarks>
internal static class HandlerInvocation
{
    /// <summary>
    ///     Invokes a main handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the handler.</typeparam>
    /// <typeparam name="TMessageResult">The result type produced by the handler.</typeparam>
    /// <param name="handler">The resolved main handler and its descriptor.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for the handler invocation.</param>
    /// <returns>The task produced by the handler.</returns>
    [RequiresUnreferencedCode("Main handler dispatch closes handler contracts over registered message types.")]
    public static Task<TMessageResult> InvokeMainHandlerAsync<TMessage, TMessageResult>(
        LazyHandler<IMessageHandler, IMainHandlerDescriptor> handler,
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var instance = handler.Handler.Value;

        if (handler.Descriptor.MessageType == typeof(TMessage))
        {
            if (instance is IAsyncMessageHandler<TMessage, TMessageResult> asyncHandler)
            {
                return asyncHandler.HandleAsync(message, cancellationToken);
            }

            if (instance is IMessageHandler<TMessage, Task<TMessageResult>> typedHandler)
            {
                return typedHandler.Handle(message);
            }
        }

        return (Task<TMessageResult>) MainHandlerDispatch.Invoke(handler.Descriptor, instance, message, cancellationToken);
    }

    /// <summary>
    ///     Invokes a void main handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the handler.</typeparam>
    /// <param name="handler">The resolved main handler and its descriptor.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for the handler invocation.</param>
    /// <returns>The task produced by the handler.</returns>
    [RequiresUnreferencedCode("Main handler dispatch closes handler contracts over registered message types.")]
    public static Task InvokeMainHandlerAsync<TMessage>(
        LazyHandler<IMessageHandler, IMainHandlerDescriptor> handler,
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var instance = handler.Handler.Value;

        if (handler.Descriptor.MessageType == typeof(TMessage))
        {
            if (instance is IAsyncMessageHandler<TMessage> asyncHandler)
            {
                return asyncHandler.HandleAsync(message, cancellationToken);
            }

            if (instance is IMessageHandler<TMessage, Task> typedHandler)
            {
                return typedHandler.Handle(message);
            }
        }

        return (Task) MainHandlerDispatch.Invoke(handler.Descriptor, instance, message, cancellationToken);
    }

    /// <summary>
    ///     Invokes a stream handler with the supplied cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the handler.</typeparam>
    /// <typeparam name="TMessageResult">The streamed result type produced by the handler.</typeparam>
    /// <param name="handler">The resolved stream handler and its descriptor.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for stream enumeration.</param>
    /// <returns>The asynchronous stream produced by the handler.</returns>
    [RequiresUnreferencedCode("Main handler dispatch closes handler contracts over registered message types.")]
    public static IAsyncEnumerable<TMessageResult> InvokeStreamHandler<TMessage, TMessageResult>(
        LazyHandler<IMessageHandler, IMainHandlerDescriptor> handler,
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var instance = handler.Handler.Value;

        if (handler.Descriptor.MessageType == typeof(TMessage))
        {
            if (instance is IStreamMessageHandler<TMessage, TMessageResult> streamHandler)
            {
                return streamHandler.StreamAsync(message, cancellationToken);
            }

            if (instance is IMessageHandler<TMessage, IAsyncEnumerable<TMessageResult>> typedHandler)
            {
                return typedHandler.Handle(message);
            }
        }

        return (IAsyncEnumerable<TMessageResult>) MainHandlerDispatch.Invoke(
            handler.Descriptor,
            instance,
            message,
            cancellationToken);
    }
}
