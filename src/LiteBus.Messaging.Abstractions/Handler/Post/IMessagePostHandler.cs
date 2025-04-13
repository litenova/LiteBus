namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents a post-handler for messages, which engages in actions or operations to be performed after a message has been initially handled.
/// </summary>
public interface IMessagePostHandler
{
    /// <summary>
    /// Facilitates the post-processing of a message following its initial handling. This method can be used to perform operations such as logging, altering the message result based on certain criteria, or triggering subsequent workflows.
    /// </summary>
    /// <param name="message">The original message that has undergone initial handling. This parameter contains the message details that were handled initially.</param>
    /// <param name="messageResult">The outcome produced from the initial handling of the message. It contains the results or state derived from the initial message handling process.</param>
    /// <returns>An object representing any further processing result or a potentially modified version of the initial message result. This return value can be utilized to convey additional information or alterations stemming from the post-handling process.</returns>
    object PostHandle(object message, object? messageResult);
}