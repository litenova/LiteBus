using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a message payload cannot be serialized or deserialized.
/// </summary>
[Serializable]
public sealed class MessageSerializationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageSerializationException" /> class for a CLR message type.
    /// </summary>
    /// <param name="messageType">The message type involved in the serialization operation.</param>
    /// <param name="operation">The serialization operation name.</param>
    /// <param name="innerException">The exception raised by the serializer.</param>
    public MessageSerializationException(Type messageType, string operation, Exception innerException)
        : base(BuildMessage(messageType.FullName ?? messageType.Name, null, null, operation), innerException)
    {
        MessageType = messageType;
        Operation = operation;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageSerializationException" /> class for a persisted contract.
    /// </summary>
    /// <param name="contractName">The stable contract name.</param>
    /// <param name="contractVersion">The contract version.</param>
    /// <param name="operation">The serialization operation name.</param>
    /// <param name="innerException">The exception raised by the serializer.</param>
    public MessageSerializationException(
        string contractName,
        int contractVersion,
        string operation,
        Exception innerException)
        : base(BuildMessage(null, contractName, contractVersion, operation), innerException)
    {
        ContractName = contractName;
        ContractVersion = contractVersion;
        Operation = operation;
    }

    /// <summary>
    ///     Gets the message type involved in the serialization operation.
    /// </summary>
    public Type? MessageType { get; }

    /// <summary>
    ///     Gets the contract name involved in the serialization operation.
    /// </summary>
    public string? ContractName { get; }

    /// <summary>
    ///     Gets the contract version involved in the serialization operation.
    /// </summary>
    public int? ContractVersion { get; }

    /// <summary>
    ///     Gets the serialization operation name.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    ///     Builds the exception message from the available contract or type context.
    /// </summary>
    /// <param name="messageTypeName">The CLR message type name, when known.</param>
    /// <param name="contractName">The contract name, when known.</param>
    /// <param name="contractVersion">The contract version, when known.</param>
    /// <param name="operation">The serialization operation name.</param>
    /// <returns>The formatted exception message.</returns>
    private static string BuildMessage(
        string? messageTypeName,
        string? contractName,
        int? contractVersion,
        string operation)
    {
        if (!string.IsNullOrWhiteSpace(contractName) && contractVersion is not null)
        {
            return $"Contract '{contractName}' version {contractVersion} could not be {operation}.";
        }

        return $"Message type '{messageTypeName}' could not be {operation}.";
    }
}
