using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Writes one audit record through its capture mode: with the business change, through the transactional outbox,
///     or as intent plus outcome.
/// </summary>
/// <remarks>
///     <para>
///         A record for a successful mediation is the only one that can commit with a business change, so it is the
///         only one that follows the capture mode. A denial, an invalid message, a failure, and a cancellation have
///         no business change to commit with and must survive the rollback, so they are written straight to the audit
///         trail in every mode. An intent record is written before the handler runs, in its own transaction, and
///         never claims success.
///     </para>
///     <para>
///         Under <see cref="AuditCapture.AfterCommitThroughOutbox" />, a success record is enqueued through the
///         transactional outbox in the business transaction and delivered to the audit store after commit, at least
///         once. The record id travels as the outbox message identity and the idempotency key, so a redelivered
///         record overwrites rather than duplicates.
///     </para>
/// </remarks>
internal sealed class AuditCaptureCoordinator : IAuditCaptureCoordinator
{
    /// <summary>
    ///     Receives captured records: staged in the business transaction for successes, immediately otherwise.
    /// </summary>
    private readonly IAuditTrail _trail;

    /// <summary>
    ///     Enqueues success records for delivery after commit, or <see langword="null" /> when the composition
    ///     registered no transactional outbox.
    /// </summary>
    private readonly ITransactionalOutbox? _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCaptureCoordinator" /> class.
    /// </summary>
    /// <param name="trail">The audit trail that receives captured records.</param>
    /// <param name="outbox">
    ///     The transactional outbox success records travel through under
    ///     <see cref="AuditCapture.AfterCommitThroughOutbox" />, or <see langword="null" /> when the composition
    ///     registered none. Capturing with that mode and no outbox fails the mediation with an
    ///     <see cref="AuditConfigurationException" />.
    /// </param>
    public AuditCaptureCoordinator(IAuditTrail trail, ITransactionalOutbox? outbox = null)
    {
        ArgumentNullException.ThrowIfNull(trail);

        _trail = trail;
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async ValueTask CaptureAsync(AuditRecord record, AuditCapture capture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(capture);

        AuditCaptureValidator.Validate(capture, "the resolved auditing capture");

        // A record application code wrote without an identifier gets one here, on every path, so two such records never
        // share a stored document and a mapper that keeps record.Id stays correct.
        if (record.Id == Guid.Empty)
        {
            record = record with { Id = Guid.CreateVersion7() };
        }

        if (record.Outcome == AuditOutcome.Succeeded
            && capture is AuditCapture.AfterCommitThroughOutboxCapture)
        {
            await EnqueueForDeliveryAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _trail.WriteAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Enqueues one success record through the transactional outbox for delivery after commit.
    /// </summary>
    /// <param name="record">The success record to deliver.</param>
    /// <param name="cancellationToken">The token used to cancel the enqueue.</param>
    /// <returns>A task representing the asynchronous enqueue.</returns>
    /// <exception cref="AuditConfigurationException">No transactional outbox is registered.</exception>
    private async ValueTask EnqueueForDeliveryAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        if (_outbox is null)
        {
            throw new AuditConfigurationException(
                "Auditing captures with AfterCommitThroughOutbox but no transactional outbox is registered, so "
                + "the record cannot be enqueued. Register an outbox storage package alongside auditing, or capture "
                + "with WithChange.");
        }

        var stored = record;
        var metadata = OutboxEnqueueMetadata.Immediate with
        {
            Identity = new MessageIdentity.Supplied(stored.Id),
            Idempotency = new Idempotency.Keyed(stored.Id.ToString()),
            Trace = MessageTrace.FromAmbient(),
            Tenant = string.IsNullOrWhiteSpace(stored.TenantId)
                ? TenantScope.Unscoped.Instance
                : new TenantScope.Isolated(stored.TenantId)
        };

        await _outbox.EnqueueAsync(
            OutboxEnqueueItem<AuditRecordOutboxMessage>.From(new AuditRecordOutboxMessage(stored), metadata),
            cancellationToken).ConfigureAwait(false);
    }
}
