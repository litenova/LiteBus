namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents a generic post-handler for messages that facilitates actions or operations to be performed after a message of a specified type has been handled, potentially utilizing and altering the results of the initial handling process.
/// </summary>
/// <typeparam name="TMessage">The type of the message being handled, allowing for messages of specific structures to be processed.</typeparam>
/// <typeparam name="TMessageResult">The type of the result produced after the message is handled, facilitating operations on structured results.</typeparam>
public interface IMessagePostHandler<in TMessage, in TMessageResult> : IMessagePostHandler where TMessage : notnull
{
    /// <summary>
    /// Provides a default implementation for post-processing of messages by casting the non-generic parameters to their generic counterparts and delegating the operation to the generic <see cref="PostHandle(TMessage, TMessageResult)"/> method.
    /// </summary>
    /// <param name="message">The original message that was handled, to be cast to the generic <typeparamref name="TMessage"/> type.</param>
    /// <param name="messageResult">The result produced from the initial handling of the message, to be cast to the generic <typeparamref name="TMessageResult"/> type.</param>
    /// <returns>Any further processing result or a potentially modified version of the initial message result, conveyed as an object.</returns>
    object IMessagePostHandler.PostHandle(object message, object? messageResult)
    {
        return PostHandle((TMessage) message, (TMessageResult?) messageResult);
    }

    /// <summary>
    /// Facilitates the post-processing of a message following its initial handling, offering a way to manage the next steps in a message handling pipeline with a focus on the specified generic types for the message and its results.
    /// </summary>
    /// <param name="message">The original message that was handled, defined with a specific structure dictated by the <typeparamref name="TMessage"/> type.</param>
    /// <param name="messageResult">The result produced from the initial handling of the message, structured according to the <typeparamref name="TMessageResult"/> type.</param>
    /// <returns>An object representing any further processing result or a potentially modified version of the initial message result, allowing for specific post-handling operations based on the generic types.</returns>
    object PostHandle(TMessage message, TMessageResult? messageResult);
}