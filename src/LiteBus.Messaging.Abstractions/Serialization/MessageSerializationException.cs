using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a durable message payload cannot be serialized or deserialized.
/// </summary>
[Serializable]
public sealed class MessageSerializationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageSerializationException" /> class.
    /// </summary>
    /// <param name="messageType">The message type involved in the serialization operation.</param>
    /// <param name="operation">The serialization operation name.</param>
    /// <param name="innerException">The exception raised by the serializer.</param>
    public MessageSerializationException(Type messageType, string operation, Exception innerException)
        : base($"The message type '{messageType.FullName ?? messageType.Name}' could not be {operation}.", innerException)
    {
        MessageType = messageType;
        Operation = operation;
    }

    /// <summary>
    ///     Gets the message type involved in the serialization operation.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Gets the serialization operation name.
    /// </summary>
    public string Operation { get; }
}