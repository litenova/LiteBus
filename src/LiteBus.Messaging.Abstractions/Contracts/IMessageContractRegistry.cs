using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Resolves durable message contracts in both directions.
/// </summary>
/// <remarks>
///     <para>
///         Writers use <see cref="GetContract" /> to convert a CLR message type to the stable name and version stored in
///         an inbox or outbox envelope. Processors and dispatchers use <see cref="GetMessageType" /> to convert the
///         stored contract back to the CLR type required for deserialization.
///     </para>
/// </remarks>
public interface IMessageContractRegistry
{
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