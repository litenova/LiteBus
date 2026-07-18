using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when explicit contract registration disagrees with <see cref="MessageContractAttribute" /> on the same CLR
///     type.
/// </summary>
public sealed class MessageContractMismatchException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageContractMismatchException" /> class.
    /// </summary>
    /// <param name="messageType">The CLR message type with conflicting contract metadata.</param>
    /// <param name="attributeName">The contract name declared by the attribute.</param>
    /// <param name="attributeVersion">The contract version declared by the attribute.</param>
    /// <param name="registeredName">The contract name supplied to <see cref="IContractWriter.Register(Type, string, int)" />.</param>
    /// <param name="registeredVersion">The contract version supplied to <see cref="IContractWriter.Register(Type, string, int)" />.</param>
    public MessageContractMismatchException(
        Type messageType,
        string attributeName,
        int attributeVersion,
        string registeredName,
        int registeredVersion)
        : base(
            $"Message type '{messageType.FullName ?? messageType.Name}' declares [MessageContract(\"{attributeName}\", {attributeVersion})] " +
            $"but was registered as '{registeredName}' version {registeredVersion}. Remove one source of truth or align both values.")
    {
        MessageType = messageType;
    }

    /// <summary>
    ///     Gets the CLR message type with conflicting contract metadata.
    /// </summary>
    public Type MessageType { get; }
}