using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Registers stable names and versions for messages that can be persisted by durable features.
/// </summary>
/// <remarks>
///     <para>
///         Durable inbox and outbox stores write contract names and integer versions instead of assembly-qualified CLR
///         type names. Register every persisted command or event at startup before scheduling commands or adding outbox
///         messages.
///     </para>
///     <para>
///         Closed generic message types are supported. Register each closed generic shape separately because it has its
///         own serialized payload contract. Open generic definitions are rejected.
///     </para>
/// </remarks>
public interface IMessageContractRegistrar
{
    /// <summary>
    ///     Registers a message type with a stable contract name and version.
    /// </summary>
    /// <typeparam name="TMessage">The concrete message type to register.</typeparam>
    /// <param name="name">The stable contract name stored in durable envelopes.</param>
    /// <param name="version">The positive contract version stored with the payload.</param>
    /// <returns>The registrar so module builders can chain registrations.</returns>
    IMessageContractRegistrar Register<TMessage>(string name, int version = 1)
        where TMessage : notnull;

    /// <summary>
    ///     Registers a message type with a stable contract name and version.
    /// </summary>
    /// <param name="messageType">The concrete message type to register.</param>
    /// <param name="name">The stable contract name stored in durable envelopes.</param>
    /// <param name="version">The positive contract version stored with the payload.</param>
    /// <returns>The registrar so module builders can chain registrations.</returns>
    IMessageContractRegistrar Register(Type messageType, string name, int version = 1);
}