using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes the stable contract for one concrete CLR message type.
/// </summary>
/// <remarks>
///     <para>
///         Durable stores persist <see cref="Name" /> and <see cref="Version" /> with the serialized payload. They do
///         not persist assembly-qualified type names. This lets applications rename or move CLR types while keeping the
///         storage contract explicit through startup registration.
///     </para>
/// </remarks>
public sealed record MessageContract
{
    /// <summary>
    ///     Gets the stable contract name written to durable envelopes.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Gets the positive contract version written to durable envelopes.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    ///     Gets the concrete CLR message type associated with the persisted contract.
    /// </summary>
    public required Type MessageType { get; init; }
}