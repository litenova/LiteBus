using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Writes outbox-delivered audit records to the audit store.
/// </summary>
/// <remarks>
///     <para>
///         Under <see cref="AuditCapture.AfterCommitThroughOutbox" />, the transactional outbox delivers each
///         enqueued record after the business transaction commits, at least once. The audit store holds one document
///         per record id, so a redelivered record overwrites rather than duplicates.
///     </para>
///     <para>
///         Delivery runs as its own mediation: it carries no business change, so the record is written straight to
///         the store rather than captured again. Enqueuing it again would deliver it again without end.
///     </para>
/// </remarks>
public sealed class AuditRecordOutboxHandler : IEventHandler<AuditRecordOutboxMessage>
{
    /// <summary>
    ///     Receives the delivered records.
    /// </summary>
    /// <remarks>
    ///     This is the audit store trail, not the capturing trail: a delivered record is written, never captured
    ///     again.
    /// </remarks>
    private readonly IAuditTrail _trail;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditRecordOutboxHandler" /> class.
    /// </summary>
    /// <param name="trail">The audit store trail that receives delivered records.</param>
    public AuditRecordOutboxHandler(IAuditTrail trail)
    {
        ArgumentNullException.ThrowIfNull(trail);

        _trail = trail;
    }

    /// <inheritdoc />
    public async Task HandleAsync(AuditRecordOutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _trail.WriteAsync(message.Record, cancellationToken).ConfigureAwait(false);
    }
}
