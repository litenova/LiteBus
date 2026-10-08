using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions;
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
    ///     Gets the serialized message payload. Relational stores persist this value as opaque text.
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
    ///     Gets the monotonic fencing generation assigned by the most recent lease acquisition.
    /// </summary>
    public long LeaseGeneration { get; init; }

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
    ///     Gets the UTC timestamp of the most recent lease, which is the last time a processor attempted the message.
    /// </summary>
    /// <value>
    ///     The timestamp, or <see langword="null" /> when the message has never been leased.
    /// </value>
    public DateTimeOffset? LastAttemptedAt { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp of the first failed attempt.
    /// </summary>
    /// <value>
    ///     The timestamp, or <see langword="null" /> when no attempt has failed. It is not reset by later successes,
    ///     so it answers how long a message struggled before it succeeded or gave up.
    /// </value>
    public DateTimeOffset? FirstFailedAt { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp at which the message was moved to the dead-letter state.
    /// </summary>
    /// <value>
    ///     The timestamp, or <see langword="null" /> when the message has not been dead-lettered. This is the field a
    ///     dead-letter listing sorts by, and it is cleared when the message is requeued.
    /// </value>
    public DateTimeOffset? DeadLetteredAt { get; init; }

    /// <summary>
    ///     Gets the processor instance that most recently held the lease.
    /// </summary>
    /// <value>
    ///     The lease owner, or <see langword="null" /> when the message has never been leased. Unlike
    ///     <see cref="LeaseOwner" />, this survives the lease being released, so it still names the instance that was
    ///     handling a message when it failed.
    /// </value>
    public string? LastLeaseOwner { get; init; }

    /// <summary>
    ///     Gets the CLR type name of the exception behind <see cref="LastError" />.
    /// </summary>
    /// <value>
    ///     The type name, or <see langword="null" /> when the message has not failed or the failure carried no
    ///     exception. Stored apart from <see cref="LastError" /> so failures can be grouped without parsing text.
    /// </value>
    public string? ErrorType { get; init; }

    /// <summary>
    ///     Returns a new envelope released from its lease without charging the attempt the lease consumed.
    /// </summary>
    /// <returns>
    ///     The envelope in <see cref="OutboxStatus.Pending" />, or <see cref="OutboxStatus.Failed" /> when it already
    ///     carries an error, with its lease cleared and its attempt count decremented. Visibility is unchanged.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    public OutboxEnvelope AsReleased()
    {
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = LastError is null ? OutboxStatus.Pending : OutboxStatus.Failed,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            AttemptCount = Math.Max(0, AttemptCount - 1)
        };
    }

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
            LastLeaseOwner = leaseOwner,
            LeaseGeneration = checked(LeaseGeneration + 1),
            LeaseExpiresAt = leaseExpiresAt,
            AttemptCount = AttemptCount + 1
        };
    }

    /// <summary>
    ///     Returns a new envelope representing successful publication.
    /// </summary>
    /// <param name="publishedAt">The UTC timestamp when publication completed.</param>
    /// <returns>The envelope after successful publication.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    public OutboxEnvelope AsPublished(DateTimeOffset publishedAt)
    {
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = OutboxStatus.Published,
            LastError = null,
            PublishedAt = publishedAt
        };
    }

    /// <summary>
    ///     Returns a new envelope representing a failed dispatch attempt,
    ///     scheduling the next visibility window via <paramref name="visibleAfter" />.
    /// </summary>
    /// <param name="failure">The failure captured for this attempt.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the envelope may be leased again.</param>
    /// <returns>The envelope scheduled for retry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failure" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    /// <remarks>
    ///     <see cref="FirstFailedAt" /> is set only on the first failure, so it keeps naming the moment the message
    ///     started struggling rather than the moment it last did.
    /// </remarks>
    public OutboxEnvelope AsFailed(MessageFailure failure, DateTimeOffset? visibleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = OutboxStatus.Failed,
            VisibleAfter = visibleAfter,
            LastError = failure.Error,
            ErrorType = failure.ErrorType,
            FirstFailedAt = FirstFailedAt ?? failure.OccurredAt
        };
    }

    /// <summary>
    ///     Returns a new envelope moved to the dead-letter state after retries are exhausted.
    /// </summary>
    /// <param name="failure">The failure that exhausted the message's attempts.</param>
    /// <returns>The dead-lettered envelope.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failure" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The envelope is not in the <see cref="OutboxStatus.Publishing" /> state.
    /// </exception>
    public OutboxEnvelope AsDeadLettered(MessageFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        EnsureStatus(OutboxStatus.Publishing);

        return this with
        {
            Status = OutboxStatus.DeadLettered,
            LastError = failure.Error,
            ErrorType = failure.ErrorType,
            FirstFailedAt = FirstFailedAt ?? failure.OccurredAt,
            DeadLetteredAt = failure.OccurredAt
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
            LastError = null,
            ErrorType = null,
            FirstFailedAt = null,
            DeadLetteredAt = null
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
