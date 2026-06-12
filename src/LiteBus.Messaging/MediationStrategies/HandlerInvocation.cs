using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Invokes message pipeline handlers with an explicit cancellation token instead of relying on ambient context timing.
/// </summary>
internal static class HandlerInvocation
{
    /// <summary>
    ///     Invokes a main handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the handler.</typeparam>
    /// <typeparam name="TMessageResult">The result type produced by the handler.</typeparam>
    /// <param name="handler">The resolved main handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for the handler invocation.</param>
    /// <returns>The task produced by the handler.</returns>
    public static Task<TMessageResult> InvokeMainHandlerAsync<TMessage, TMessageResult>(
        IMessageHandler handler,
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        if (handler is IAsyncMessageHandler<TMessage, TMessageResult> asyncHandler)
        {
            return asyncHandler.HandleAsync(message, cancellationToken);
        }

        if (handler is IMessageHandler<TMessage, Task<TMessageResult>> typedHandler)
        {
            return typedHandler.Handle(message);
        }

        return (Task<TMessageResult>) handler.Handle(message);
    }

    /// <summary>
    ///     Invokes a void main handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the handler.</typeparam>
    /// <param name="handler">The resolved main handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for the handler invocation.</param>
    /// <returns>The task produced by the handler.</returns>
    public static Task InvokeMainHandlerAsync<TMessage>(
        IMessageHandler handler,
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        if (handler is IAsyncMessageHandler<TMessage> asyncHandler)
        {
            return asyncHandler.HandleAsync(message, cancellationToken);
        }

        if (handler is IMessageHandler<TMessage, Task> typedHandler)
        {
            return typedHandler.Handle(message);
        }

        return (Task) handler.Handle(message);
    }

    /// <summary>
    ///     Invokes a stream handler with the supplied cancellation token.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the handler.</typeparam>
    /// <typeparam name="TMessageResult">The streamed result type produced by the handler.</typeparam>
    /// <param name="handler">The resolved stream handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for stream enumeration.</param>
    /// <returns>The asynchronous stream produced by the handler.</returns>
    public static IAsyncEnumerable<TMessageResult> InvokeStreamHandler<TMessage, TMessageResult>(
        IMessageHandler handler,
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        if (handler is IStreamMessageHandler<TMessage, TMessageResult> streamHandler)
        {
            return streamHandler.StreamAsync(message, cancellationToken);
        }

        if (handler is IMessageHandler<TMessage, IAsyncEnumerable<TMessageResult>> typedHandler)
        {
            return typedHandler.Handle(message);
        }

        return (IAsyncEnumerable<TMessageResult>) handler.Handle(message);
    }

}
