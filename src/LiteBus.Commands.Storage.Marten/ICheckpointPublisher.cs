using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Publishes audit chain checkpoints to an independent store.
/// </summary>
/// <remarks>
///     <para>
///         The port half of the checkpointed audit trail: the trail calls it every
///         <see cref="MartenAuditTrailOptions.CheckpointInterval" /> records of a segment with the sequence number and
///         hash the chain has reached. Where the checkpoint goes is the application's decision, a separate table, a
///         ledger, or a compliance store, so this contract is deliberately small. Publish synchronously: a checkpoint
///         the application never stored anchors nothing.
///     </para>
///     <para>
///         A publisher that throws fails the audit write it was called from, so a lost checkpoint surfaces instead of
///         passing silently.
///     </para>
/// </remarks>
public interface ICheckpointPublisher
{
    /// <summary>
    ///     Publishes one audit chain checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint the chain reached.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns>A task representing the asynchronous publish.</returns>
    ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default);
}
