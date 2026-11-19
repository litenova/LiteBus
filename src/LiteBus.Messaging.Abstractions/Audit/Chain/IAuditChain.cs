using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Operates on a hash-chained audit trail: verification, checkpoints and segment expiry.
/// </summary>
/// <remarks>
///     A storage adapter that writes a hash chain implements this contract, so operational code verifies, anchors and
///     expires segments the same way on every store.
/// </remarks>
public interface IAuditChain
{
    /// <summary>
    ///     Verifies one segment and compares it against its latest checkpoint.
    /// </summary>
    /// <param name="segment">The segment to verify.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>Every break found, the head, and the checkpoint comparison.</returns>
    Task<AuditChainVerification> VerifyAsync(string segment, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Cuts, stores, signs and publishes a checkpoint of a segment now.
    /// </summary>
    /// <param name="segment">The segment to checkpoint.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The checkpoint that was cut.</returns>
    Task<AuditCheckpoint> CutCheckpointAsync(string segment, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Publishes a final checkpoint for a segment, then deletes the segment's documents and closes it to new writes.
    /// </summary>
    /// <param name="segment">The segment to expire.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The final checkpoint.</returns>
    /// <remarks>
    ///     Retention-driven deletion uses this so the remaining segments still verify: each segment is an independent
    ///     chain, and the final checkpoint proves what the deleted segment held.
    /// </remarks>
    Task<AuditCheckpoint> ExpireSegmentAsync(string segment, CancellationToken cancellationToken = default);
}
