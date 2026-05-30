using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents the result of accepting a message into the inbox.
/// </summary>
/// <remarks>
///     <para>
///         A receipt means the store accepted the inbox envelope. It does not mean a handler has run. Return this value
///         from an API as acceptance or tracking data, then use queries to read the state produced by later execution.
///     </para>
/// </remarks>
/// <typeparam name="T">The message type associated with the receipt.</typeparam>
public sealed record InboxReceipt<T>
    where T : notnull
{
    /// <summary>
    ///     Gets the unique message identifier that processors and tracking endpoints can use.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the CLR message type that was accepted. For closed generic messages, this is the closed runtime type.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the stable message contract name stored with the payload.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the contract version used when the message was serialized.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the message was accepted by the store.
    /// </summary>
    public required DateTimeOffset AcceptedAt { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier copied from scheduling options or from the stored duplicate row.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier copied from scheduling options or from the stored duplicate row.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier copied from scheduling options or from the stored duplicate row.
    /// </summary>
    public string? TenantId { get; init; }
}