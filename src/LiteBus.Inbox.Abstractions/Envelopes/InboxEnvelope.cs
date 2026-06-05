using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents one persisted inbox row in a store.
/// </summary>
/// <remarks>
///     Store implementations use this non-generic envelope so one table can hold many message types. The payload is the
///     serialized message; the contract fields identify the CLR type used for deserialization. Processors update status,
///     attempt count, lease, and error fields as the envelope moves through execution.
/// </remarks>
public sealed record InboxEnvelope
{
    /// <summary>
    ///     Gets the unique persisted message identifier.
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
    ///     Gets the UTC timestamp when the message was accepted.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp at which the message may be processed.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }

    /// <summary>
    ///     Gets the number of processing attempts. Stores increment this value when a message is leased.
    /// </summary>
    public required int AttemptCount { get; init; }

    /// <summary>
    ///     Gets the current processing status.
    /// </summary>
    public required InboxStatus Status { get; init; }

    /// <summary>
    ///     Gets the optional idempotency key used to detect duplicate submissions.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    ///     Gets the optional processing lease owner that currently holds the message.
    /// </summary>
    public string? LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the optional UTC timestamp when the processing lease expires.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>
    ///     Gets the optional latest processing error captured for diagnostics and dead-letter review.
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
    ///     Gets the optional distributed trace context stored as JSON text (for example W3C trace context or OpenTelemetry baggage).
    /// </summary>
    public string? TraceContext { get; init; }

    /// <summary>
    ///     Returns a new envelope in the completed state with lease and error fields cleared.
    /// </summary>
    /// <returns>The envelope after successful processing.</returns>
    public InboxEnvelope AsCompleted() =>
        this with
        {
            Status = InboxStatus.Completed,
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
    public InboxEnvelope AsFailed(string error, DateTimeOffset? visibleAfter = null) =>
        this with
        {
            Status = InboxStatus.Failed,
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
    public InboxEnvelope AsDeadLettered(string reason) =>
        this with
        {
            Status = InboxStatus.DeadLettered,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = reason
        };

    /// <summary>
    ///     Returns a new envelope reset to pending state after dead-letter review.
    /// </summary>
    /// <returns>The requeued envelope.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not currently in the <see cref="InboxStatus.DeadLettered" /> state.
    /// </exception>
    public InboxEnvelope AsRequeued()
    {
        if (Status != InboxStatus.DeadLettered)
        {
            throw new InvalidOperationException(
                $"Only dead-lettered messages can be requeued. Current status: {Status}.");
        }

        return this with
        {
            Status = InboxStatus.Pending,
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
    public InboxEnvelope AsLeased(string leaseOwner, DateTimeOffset leaseExpiresAt) =>
        this with
        {
            Status = InboxStatus.Processing,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
            AttemptCount = AttemptCount + 1
        };
}
