namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     An alias for <see cref="IMessagePreHandler{TMessage}" />, retained so existing handlers keep compiling.
/// </summary>
/// <typeparam name="TMessage">The type of message this pre-handler runs for.</typeparam>
/// <remarks>
///     The distinction between a synchronous and an asynchronous pre-handler no longer exists: every pre-handler is
///     asynchronous. Prefer <see cref="IMessagePreHandler{TMessage}" /> in new code.
/// </remarks>
public interface IAsyncMessagePreHandler<in TMessage> : IMessagePreHandler<TMessage>
    where TMessage : notnull;
