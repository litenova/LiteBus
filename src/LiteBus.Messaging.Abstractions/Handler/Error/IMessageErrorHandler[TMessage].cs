namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Handles an exception raised while mediating a message of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The type of message this error handler runs for.</typeparam>
/// <remarks>
///     This is <see cref="IMessageErrorHandler{TMessage,TMessageResult}" /> closed over an untyped result, for a message
///     that produces none. A decision is not a fault, so this stage never sees a guard refusal or a validation failure.
/// </remarks>
public interface IMessageErrorHandler<TMessage> : IMessageErrorHandler<TMessage, object>
    where TMessage : notnull;
