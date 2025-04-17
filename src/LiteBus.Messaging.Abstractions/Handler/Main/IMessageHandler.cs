namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the non-generic base interface for all message handlers, facilitating handling operations for messages
///     of any type.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    ///     Handles the incoming message and facilitates the necessary operations or transformations as defined by the specific
    ///     handler implementation.
    /// </summary>
    /// <param name="message">The message to be handled, represented as an object which can be of any type.</param>
    /// <returns>
    ///     An object representing the outcome of the handling operation, which might include results of processing,
    ///     transformed message, or other related data depending on the specific handler's implementation.
    /// </returns>
    object Handle(object message);
}