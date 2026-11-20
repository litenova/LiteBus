using System.Threading;
using LiteBus.Auditing.Abstractions;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Cuts checkpoints, publishes pending ones, and cuts the checkpoints policies report due.
/// </summary>
internal interface ICheckpointLifecycle
{
    /// <summary>
    ///     Cuts a checkpoint of a segment now and stores it as pending.
    /// </summary>
    /// <param name="segment">The segment to checkpoint.</param>
    /// <param name="label">The free-form label stored on the checkpoint, or <see langword="null" /> for none.</param>
    /// <param name="kind">Why the checkpoint is cut.</param>
    /// <param name="cancellationToken">The token used to cancel the cut.</param>
    /// <returns>The checkpoint that was cut.</returns>
    ValueTask<AuditCheckpoint> CutAsync(
        string segment,
        string? label,
        AuditCheckpointKind kind,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Publishes every stored checkpoint still waiting for publication.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the publication.</param>
    /// <returns>The number of checkpoints published.</returns>
    ValueTask<int> PublishPendingAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Cuts a checkpoint for every segment whose policy reports one due.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the cuts.</param>
    /// <returns>A task representing the asynchronous cuts.</returns>
    ValueTask CutDueAsync(CancellationToken cancellationToken);
}
