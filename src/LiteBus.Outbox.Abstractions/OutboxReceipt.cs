using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents the result of accepting an event into the outbox.
/// </summary>
/// <remarks>
///     <para>
///         A receipt means the store accepted the outbox envelope. It does not mean a dispatcher has published the event
///         to its final target. Use the message id for diagnostics, replay tooling, or API acceptance responses.
///     </para>
/// </remarks>
/// <typeparam name="T">The message type associated with the receipt.</typeparam>
public sealed record OutboxReceipt<T>
    where T : notnull
{
    /// <summary>
    ///     Gets the unique outbox message identifier used by processors and operational tooling.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the CLR message type that was stored. For closed generic types, this is the closed runtime type.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the stable event contract name stored with the payload.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the event contract version used when the payload was serialized.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the event was accepted by the store.
    /// </summary>
    public required DateTimeOffset StoredAt { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier copied from outbox options or from the stored duplicate row.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier copied from outbox options or from the stored duplicate row.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier copied from outbox options or from the stored duplicate row.
    /// </summary>
    public string? TenantId { get; init; }
}