using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Registers and resolves stable contract names and versions for messages persisted by inbox and outbox features.
/// </summary>
/// <remarks>
///     <para>
///         Register every persisted command or event at startup before scheduling commands or adding outbox messages.
///         Inbox and outbox stores write contract names and integer versions instead of assembly-qualified CLR type names.
///     </para>
///     <para>
///         Writers use <see cref="GetContract" /> to convert a CLR message type to the stable name and version stored in
///         an inbox or outbox envelope. Processors and dispatchers use <see cref="GetMessageType" /> to convert the
///         stored contract back to the CLR type required for deserialization.
///     </para>
///     <para>
///         Closed generic message types are supported. Register each closed generic shape separately because it has its
///         own serialized payload contract. Open generic definitions are rejected.
///     </para>
/// </remarks>
public interface IMessageContractRegistry
{
    /// <summary>
    ///     Registers a message type with a stable contract name and version.
    /// </summary>
    /// <typeparam name="TMessage">The concrete message type to register.</typeparam>
    /// <param name="name">The stable contract name stored in inbox and outbox envelopes.</param>
    /// <param name="version">The positive contract version stored with the payload.</param>
    /// <returns>The registry so module builders can chain registrations.</returns>
    IMessageContractRegistry Register<TMessage>(string name, int version = 1)
        where TMessage : notnull;

    /// <summary>
    ///     Registers a message type with a stable contract name and version.
    /// </summary>
    /// <param name="messageType">The concrete message type to register.</param>
    /// <param name="name">The stable contract name stored in inbox and outbox envelopes.</param>
    /// <param name="version">The positive contract version stored with the payload.</param>
    /// <returns>The registry so module builders can chain registrations.</returns>
    IMessageContractRegistry Register(Type messageType, string name, int version = 1);

    /// <summary>
    ///     Gets the persisted contract for a CLR message type.
    /// </summary>
    /// <param name="messageType">The concrete CLR message type. Closed generic types must be registered directly.</param>
    /// <returns>The registered message contract.</returns>
    MessageContract GetContract(Type messageType);

    /// <summary>
    ///     Gets the CLR message type for a persisted contract.
    /// </summary>
    /// <param name="contractName">The stable contract name stored in the envelope.</param>
    /// <param name="contractVersion">The contract version stored with the payload.</param>
    /// <returns>The registered CLR message type.</returns>
    Type GetMessageType(string contractName, int contractVersion);
}
