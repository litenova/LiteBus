using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Commands.Batching;

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
    ///     Tells whether the scope belongs to a batch and holds the batch's buffered audit records.
    /// </summary>
    private readonly CommandBatchContext _batch;

    /// <summary>
    ///     Receives the flushed records, or null when the application registered no trail.
    /// </summary>
    private readonly IAuditTrail? _trail;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandBatchAuditParticipant" /> class.
    /// </summary>
    /// <param name="batch">The batch context holding the buffered audit records.</param>
    /// <param name="trail">
    ///     The trail that receives the flushed records, or <see langword="null" /> when auditing is not enabled.
    /// </param>
    public CommandBatchAuditParticipant(CommandBatchContext batch, IAuditTrail? trail = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        _batch = batch;
        _trail = trail;
    }

    /// <inheritdoc />
    public async Task BeforeCommitAsync(CancellationToken cancellationToken)
    {
        var trail = _trail;
        var buffer = _batch.AuditRecords;

        if (trail is null || buffer is null)
        {
            return;
        }

        foreach (var record in buffer.Records)
        {
            await trail.WriteAsync(record, cancellationToken).ConfigureAwait(false);
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
        var buffer = _batch.AuditRecords;

        if (trail is null || buffer is null)
        {
            return;
        }

        foreach (var record in buffer.RewriteForRollback())
        {
            await trail.WriteAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }
}
