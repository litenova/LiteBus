namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents a generic interface defining a pre-handler for messages of a specified type, intended to preprocess messages before they are handled by the primary message handler.
/// </summary>
/// <typeparam name="TMessage">The type of the message that is to be pre-handled, allowing for type-specific pre-handling logic to be implemented.</typeparam>
public interface IMessagePreHandler<in TMessage> : IMessagePreHandler where TMessage : notnull
{
    /// <summary>
    /// Implements the pre-processing logic for a message using a specific type, before it is handled by the primary handler. This method is invoked internally to cast the message to the correct type and delegate the pre-handling to the type-specific <see cref="PreHandle(TMessage)"/> method.
    /// </summary>
    /// <param name="message">The original message to be pre-processed, presented as an object which will be cast to the type-specific representation for further processing.</param>
    /// <returns>The result of the pre-processing, which may be a transformed version of the original message or other data derived from the pre-processing actions, ready to be passed on to the primary handler.</returns>
    object IMessagePreHandler.PreHandle(object message)
    {
        return PreHandle((TMessage) message);
    }

    /// <summary>
    /// Defines the pre-processing actions to be taken on a message of a specified type before it undergoes handling by the primary message handler. This method should encapsulate the logic necessary to prepare the message for subsequent handling.
    /// </summary>
    /// <param name="message">The original message of type <typeparamref name="TMessage"/> that requires pre-processing.</param>
    /// <returns>The outcome of the pre-processing, potentially modifying the message or producing other data as necessary for the subsequent handling stages.</returns>
    object PreHandle(TMessage message);
}