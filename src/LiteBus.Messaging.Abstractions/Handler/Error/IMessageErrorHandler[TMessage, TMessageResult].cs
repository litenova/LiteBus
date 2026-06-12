namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a contract for a message error handler that operates on messages of specified types, providing mechanisms
///     for handling errors that occur during message processing.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this handler can process.</typeparam>
/// <typeparam name="TMessageResult">The type of the message result that this handler deals with.</typeparam>
public interface IMessageErrorHandler<in TMessage, in TMessageResult> : IMessageErrorHandler where TMessage : notnull;
