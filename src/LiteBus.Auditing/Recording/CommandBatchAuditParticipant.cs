using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Abstractions;
using LiteBus.Commands.Abstractions;

namespace LiteBus.Auditing.Recording;

/// <summary>
///     Writes a command batch's buffered audit records to the audit trail when the batch ends.
/// </summary>
/// <remarks>
///     <para>
///         Each command's record writer added its record to the batch's buffer instead of the trail. This participant
///         flushes that buffer: in order before the single commit when every command passed, so a trail that writes
///         into the same transaction stays atomic with the data, and rewritten after the rollback when the batch
///         stopped, so each command that passed is recorded as failed while the command that stopped the batch keeps
///         its own outcome. Commands that never ran left no record.
///     </para>
///     <para>
///         A trail that writes into the rolled-back transaction loses the rollback records with the transaction,
///         exactly as a failed single command loses its record when the trail rides the transaction being rolled
///         back. Write the trail out of band when those records must survive a rollback.
///     </para>
/// </remarks>
internal sealed class CommandBatchAuditParticipant : ICommandBatchParticipant
{
    /// <summary>
    ///     The buffer the batch's commands add their records to, created when the batch starts and
    ///     <see langword="null" /> before that.
    /// </summary>
    private AuditRecordBuffer? _buffer;

    /// <summary>
    ///     Receives the flushed records, or null when the application registered no trail.
    /// </summary>
    private readonly IAuditTrail? _trail;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandBatchAuditParticipant" /> class.
    /// </summary>
    /// <param name="trail">
    ///     The trail that receives the flushed records, or <see langword="null" /> when auditing is not enabled.
    /// </param>
    public CommandBatchAuditParticipant(IAuditTrail? trail = null)
    {
        _trail = trail;
    }

    /// <inheritdoc />
    public Task BeginAsync(CommandBatchStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);

        _buffer = new AuditRecordBuffer(start.BatchId);
        start.CommandItems[AuditRecordBuffer.ItemKey] = _buffer;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task BeforeCommitAsync(CancellationToken cancellationToken)
    {
        var trail = _trail;
        var buffer = _buffer;

        if (trail is null || buffer is null)
        {
            return;
        }

        foreach (var record in buffer.Records)
        {
            await WriteAsync(trail, buffer, record, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task AfterCommitAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var trail = _trail;
        var buffer = _buffer;

        if (trail is null || buffer is null)
        {
            return;
        }

        foreach (var record in buffer.RewriteForRollback())
        {
            await WriteAsync(trail, buffer, record, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Writes one buffered record in the mapping context of the command that produced it.
    /// </summary>
    /// <param name="trail">The trail that receives the record.</param>
    /// <param name="buffer">The buffer holding the record's mapping context.</param>
    /// <param name="record">The record to write.</param>
    /// <param name="cancellationToken">The token used to cancel the write.</param>
    /// <returns>A task that completes when the trail has written the record.</returns>
    private static async Task WriteAsync(
        IAuditTrail trail,
        AuditRecordBuffer buffer,
        AuditRecord record,
        CancellationToken cancellationToken)
    {
        var context = buffer.ContextOf(record);

        if (context is null)
        {
            await trail.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        using (AuditMappingScope.Enter(context))
        {
            await trail.WriteAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }
}
