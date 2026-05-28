using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a durable message contract cannot be resolved.
/// </summary>
[Serializable]
public sealed class MessageContractNotRegisteredException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageContractNotRegisteredException" /> class for a CLR type.
    /// </summary>
    /// <param name="messageType">The CLR message type.</param>
    public MessageContractNotRegisteredException(Type messageType)
        : base($"Message type '{messageType.FullName ?? messageType.Name}' has no durable contract registration.")
    {
        MessageType = messageType;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageContractNotRegisteredException" /> class for a persisted contract.
    /// </summary>
    /// <param name="contractName">The stable contract name.</param>
    /// <param name="contractVersion">The contract version.</param>
    public MessageContractNotRegisteredException(string contractName, int contractVersion)
        : base($"Message contract '{contractName}' version {contractVersion} has no CLR type registration.")
    {
        ContractName = contractName;
        ContractVersion = contractVersion;
    }

    /// <summary>
    ///     Gets the unresolved CLR message type.
    /// </summary>
    public Type? MessageType { get; }

    /// <summary>
    ///     Gets the unresolved contract name.
    /// </summary>
    public string? ContractName { get; }

    /// <summary>
    ///     Gets the unresolved contract version.
    /// </summary>
    public int? ContractVersion { get; }
}