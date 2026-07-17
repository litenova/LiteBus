namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous error handler for messages of type <typeparamref name="TMessage" />.
///     This interface should be implemented to handle exceptions that occur during message processing.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this error handler is applicable to.</typeparam>
public interface IAsyncMessageErrorHandler<TMessage> : IAsyncMessageErrorHandler<TMessage, object>
    where TMessage : notnull;
