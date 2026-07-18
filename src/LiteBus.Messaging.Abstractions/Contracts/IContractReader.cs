using System;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Read-only surface for message contract resolution.
///     Consumed by dispatchers, serializers, and envelope factories at runtime.
/// </summary>
public interface IContractReader
{
    /// <summary>
    ///     Returns the contract registered for <paramref name="messageType" />.
    /// </summary>
    /// <param name="messageType">The concrete CLR message type.</param>
    /// <returns>The registered message contract.</returns>
    /// <exception cref="MessageContractNotRegisteredException">
    ///     Thrown when no contract is registered for the type.
    /// </exception>
    MessageContract GetContract(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type messageType);

    /// <summary>
    ///     Returns the CLR type registered for the given contract name and version.
    /// </summary>
    /// <param name="contractName">The stable contract name stored in the envelope.</param>
    /// <param name="contractVersion">The contract version stored with the payload.</param>
    /// <returns>The registered CLR message type.</returns>
    /// <exception cref="MessageContractNotRegisteredException">
    ///     Thrown when no type is registered for the contract.
    /// </exception>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    Type GetMessageType(string contractName, int contractVersion);

    /// <summary>
    ///     Returns the contract for <paramref name="messageType" />,
    ///     or <see langword="null" /> if not registered.
    /// </summary>
    /// <param name="messageType">The concrete CLR message type.</param>
    /// <returns>The registered contract, or <see langword="null" /> when the type has no registration.</returns>
    MessageContract? TryGetContract(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type messageType);

    /// <summary>
    ///     Returns the CLR type for the given contract name and version,
    ///     or <see langword="null" /> if not registered.
    /// </summary>
    /// <param name="contractName">The stable contract name stored in the envelope.</param>
    /// <param name="contractVersion">The contract version stored with the payload.</param>
    /// <returns>The registered CLR type, or <see langword="null" /> when the contract is unknown.</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    Type? TryGetMessageType(string contractName, int contractVersion);
}