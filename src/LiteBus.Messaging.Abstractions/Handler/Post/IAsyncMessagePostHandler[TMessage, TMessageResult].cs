namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     An alias for <see cref="IMessagePostHandler{TMessage,TMessageResult}" />, retained so existing handlers keep
///     compiling.
/// </summary>
/// <typeparam name="TMessage">The type of the message that was handled.</typeparam>
/// <typeparam name="TMessageResult">The type of the result produced by the main handler.</typeparam>
/// <remarks>
///     The distinction between a synchronous and an asynchronous post-handler no longer exists: every post-handler is
///     asynchronous. Prefer <see cref="IMessagePostHandler{TMessage,TMessageResult}" /> in new code.
/// </remarks>
public interface IAsyncMessagePostHandler<in TMessage, in TMessageResult>
    : IMessagePostHandler<TMessage, TMessageResult>
    where TMessage : notnull
    where TMessageResult : notnull;
