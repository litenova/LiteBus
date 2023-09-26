namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an interface that defines a pre-handler to process messages before the primary handling takes place.
/// </summary>
/// <remarks>
/// This interface should be implemented by classes intended to define actions or transformations on messages before they undergo the primary handling process. This could encompass operations such as validation, sanitization, or augmentation of the message data.
/// </remarks>
public interface IMessagePreHandler
{
    /// <summary>
    /// Executes the necessary pre-processing steps on a message before it undergoes the primary handling process. This can involve actions such as augmenting the message with additional data, transforming its format, or validating its contents.
    /// </summary>
    /// <param name="message">The original message that is to be pre-processed. This parameter receives the message as it was before any handling process, providing the raw data that the pre-handler can operate on.</param>
    /// <returns>An object representing the outcome of the pre-processing steps, which might be a transformed version of the original message or other results of the pre-processing actions. This output will be utilized in subsequent processing steps.</returns>
    object PreHandle(object message);
}