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

    /// <summary>
    ///     Returns a new envelope in the published state with lease and error fields cleared.
    /// </summary>
    /// <returns>The envelope after successful publication.</returns>
    public OutboxEnvelope AsPublished() =>
        this with
        {
            Status = OutboxStatus.Published,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = null
        };

    /// <summary>
    ///     Returns a new envelope in the failed state with lease fields cleared and the supplied retry visibility.
    /// </summary>
    /// <param name="error">The error captured for this attempt.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the envelope may be leased again.</param>
    /// <returns>The envelope scheduled for retry.</returns>
    public OutboxEnvelope AsFailed(string error, DateTimeOffset? visibleAfter = null) =>
        this with
        {
            Status = OutboxStatus.Failed,
            VisibleAfter = visibleAfter,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = error
        };

    /// <summary>
    ///     Returns a new envelope in the dead-lettered state with lease fields cleared.
    /// </summary>
    /// <param name="reason">The reason the envelope was moved to the dead-letter state.</param>
    /// <returns>The dead-lettered envelope.</returns>
    public OutboxEnvelope AsDeadLettered(string reason) =>
        this with
        {
            Status = OutboxStatus.DeadLettered,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = reason
        };

    /// <summary>
    ///     Returns a new envelope reset to pending state after dead-letter review.
    /// </summary>
    /// <returns>The requeued envelope.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not currently in the <see cref="OutboxStatus.DeadLettered" /> state.
    /// </exception>
    public OutboxEnvelope AsRequeued()
    {
        if (Status != OutboxStatus.DeadLettered)
        {
            throw new InvalidOperationException(
                $"Only dead-lettered messages can be requeued. Current status: {Status}.");
        }

        return this with
        {
            Status = OutboxStatus.Pending,
            VisibleAfter = null,
            AttemptCount = 0,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = null
        };
    }

    /// <summary>
    ///     Returns a new envelope with an active lease and an incremented attempt count.
    /// </summary>
    /// <param name="leaseOwner">The processor instance that claimed the envelope.</param>
    /// <param name="leaseExpiresAt">The UTC timestamp when the lease expires.</param>
    /// <returns>The leased envelope returned to the processor.</returns>
    public OutboxEnvelope AsLeased(string leaseOwner, DateTimeOffset leaseExpiresAt) =>
        this with
        {
            Status = OutboxStatus.Publishing,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
            AttemptCount = AttemptCount + 1
        };
}
