using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one persisted event row in a durable outbox store.
/// </summary>
/// <remarks>
///     Store implementations use this non-generic envelope so one table can hold many event types. The payload is the
///     serialized event; the contract fields identify the CLR type used for deserialization or transport mapping.
///     Processors update status, attempt count, lease, and error fields as the message moves through publication.
/// </remarks>
public sealed record OutboxMessageEnvelope
{
    /// <summary>
    ///     Gets the unique outbox message identifier.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>
    ///     Gets the stable event contract name used to resolve the event type.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the event contract version used to resolve the event type and payload shape.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the serialized event payload. The default PostgreSQL store writes this value to a `jsonb` column.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    ///     Gets the optional topic or channel used by external dispatchers.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the event was stored.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp at which the message may be published.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }

    /// <summary>
    ///     Gets the current publication status.
    /// </summary>
    public required OutboxMessageStatus Status { get; init; }

    /// <summary>
    ///     Gets the number of publication attempts. Stores increment this value when a message is leased.
    /// </summary>
    public required int AttemptCount { get; init; }

    /// <summary>
    ///     Gets the optional publication lease owner that currently holds the message.
    /// </summary>
    public string? LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the optional UTC timestamp when the publication lease expires.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>
    ///     Gets the optional latest publication error captured for diagnostics and dead-letter review.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier.
    /// </summary>
    public string? TenantId { get; init; }
}