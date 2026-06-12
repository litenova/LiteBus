using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one persisted message row in an outbox store.
/// </summary>
/// <remarks>
///     Store implementations use this non-generic envelope so one table can hold many message types. The payload is the
///     serialized message; the contract fields identify the CLR type used for deserialization or transport mapping.
///     Processors update status, attempt count, lease, and error fields as the message moves through publication.
/// </remarks>
[DebuggerDisplay("Id = {Id}, Status = {Status}, AttemptCount = {AttemptCount}")]
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
    ///     Gets how duplicate idempotency keys are handled for this enqueue attempt. The value is not persisted.
    /// </summary>
    public IdempotencyConflictMode IdempotencyConflictMode { get; init; } = IdempotencyConflictMode.ReturnExisting;

    /// <summary>
    ///     Gets the optional distributed trace context stored as JSON text (for example W3C trace context or OpenTelemetry
    ///     baggage).
    /// </summary>
    public string? TraceContext { get; init; }

    /// <summary>
    ///     Gets the optional UTC timestamp when the message reached the <see cref="OutboxStatus.Published" /> state.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>
    ///     Returns a new envelope with an active lease and an incremented attempt count.
    /// </summary>
    /// <param name="leaseOwner">The processor instance that claimed the envelope.</param>
    /// <param name="leaseExpiresAt">The UTC timestamp when the lease expires.</param>
    /// <returns>The leased envelope returned to the processor.</returns>
    public OutboxEnvelope AsLeased(string leaseOwner, DateTimeOffset leaseExpiresAt)
    {
        return this with
        {
            Status = OutboxStatus.Publishing,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
            AttemptCount = AttemptCount + 1
        };
    }

    /// <summary>
    ///     Returns a new envelope representing successful publication.
    /// </summary>
    /// <returns>The envelope after successful publication.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    public OutboxEnvelope AsPublished()
    {
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = OutboxStatus.Published,
            LastError = null,
            PublishedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    ///     Returns a new envelope representing a failed dispatch attempt,
    ///     scheduling the next visibility window via <paramref name="visibleAfter" />.
    /// </summary>
    /// <param name="error">The error captured for this attempt.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the envelope may be leased again.</param>
    /// <returns>The envelope scheduled for retry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    public OutboxEnvelope AsFailed(string error, DateTimeOffset? visibleAfter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = OutboxStatus.Failed,
            VisibleAfter = visibleAfter,
            LastError = error
        };
    }

    /// <summary>
    ///     Returns a new envelope moved to the dead-letter state after retries are exhausted.
    /// </summary>
    /// <param name="reason">The reason the envelope was moved to the dead-letter state.</param>
    /// <returns>The dead-lettered envelope.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    public OutboxEnvelope AsDeadLettered(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = OutboxStatus.DeadLettered,
            LastError = reason
        };
    }

    /// <summary>
    ///     Returns a new envelope reset to the pending state for manual replay.
    /// </summary>
    /// <returns>The requeued envelope.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.DeadLettered" /> state.
    /// </exception>
    public OutboxEnvelope AsRequeued()
    {
        EnsureStatus(OutboxStatus.DeadLettered);

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
    ///     Throws when the current status does not match the required transition source state.
    /// </summary>
    /// <param name="required">The status required before applying the transition.</param>
    private void EnsureStatus(OutboxStatus required)
    {
        if (Status != required)
        {
            throw new InvalidOperationException(
                $"Transition is not valid from status '{Status}'. Required status: '{required}'.");
        }
    }
}