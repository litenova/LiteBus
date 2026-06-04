using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one persisted message row in an outbox store.
/// </summary>
/// <remarks>
///     Store implementations use this non-generic envelope so one table can hold many message types. The payload is the
///     serialized message; the contract fields identify the CLR type used for deserialization or transport mapping.
///     Processors update status, attempt count, lease, and error fields as the message moves through publication.
/// </remarks>
public sealed record OutboxEnvelope
{
    /// <summary>
    ///     Gets the unique outbox message identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the stable message contract name used to resolve the message type.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the message contract version used to resolve the message type and payload shape.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the serialized message payload. The default PostgreSQL store writes this value to a `jsonb` column.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    ///     Gets the optional topic or channel used by external dispatchers.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the message was stored.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp at which the message may be published.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }

    /// <summary>
    ///     Gets the current publication status.
    /// </summary>
    public required OutboxStatus Status { get; init; }

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

    /// <summary>
    ///     Gets the optional idempotency key used to collapse duplicate enqueue attempts into one stored row.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    ///     Gets the optional distributed trace context stored as JSON text (for example W3C trace context or OpenTelemetry baggage).
    /// </summary>
    public string? TraceContext { get; init; }
}
