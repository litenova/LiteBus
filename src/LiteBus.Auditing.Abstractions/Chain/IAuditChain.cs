using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Operates on a hash-chained audit trail: verification, checkpoints, segments, and erasure.
/// </summary>
/// <remarks>
///     A storage adapter that writes a hash chain implements this contract, so operational code verifies, anchors,
///     lists, and erases segments the same way on every store.
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
    ///     Verifies one segment against every configured checkpoint source, streaming records by pages.
    /// </summary>
    /// <param name="request">The segment and the thresholds to verify it with.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>Every break found, the head, and one comparison per source.</returns>
    Task<AuditChainVerification> VerifyAsync(
        AuditChainVerificationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Trusts an anchored checkpoint and verifies only the records after it.
    /// </summary>
    /// <param name="segment">The segment to verify.</param>
    /// <param name="checkpointId">The identifier of the trusted checkpoint.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>Every break found after the checkpoint, the head, and the checkpoint comparison.</returns>
    Task<AuditChainVerification> VerifyFromAsync(
        string segment,
        Guid checkpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the segments matching a filter.
    /// </summary>
    /// <param name="filter">The segments to list.</param>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The matching segment summaries.</returns>
    IAsyncEnumerable<AuditSegmentSummary> ListSegmentsAsync(
        AuditSegmentFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the stored checkpoints still waiting for publication.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The checkpoints whose publish state is <see cref="CheckpointPublishState.Pending" />.</returns>
    IAsyncEnumerable<AuditCheckpoint> ListUnpublishedCheckpointsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the records, heads, and stored checkpoints matching a filter, and publishes nothing by default.
    /// </summary>
    /// <param name="filter">The segments to erase.</param>
    /// <param name="reason">Why the segments are erased, recorded with the erasure notice when notices are on.</param>
    /// <param name="cancellationToken">The token used to cancel the erasure.</param>
    /// <returns>The number of segments erased.</returns>
    /// <remarks>
    ///     Erasure is a different legal act from expiry: expiry keeps a final checkpoint proving what was removed,
    ///     while erasure leaves nothing identifying, including anchors that describe the data. An optional erasure
    ///     notice carries no hash.
    /// </remarks>
    Task<int> EraseAsync(
        AuditSegmentFilter filter,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Cuts, stores, and signs a checkpoint of a segment now; publication follows from the background retrier.
    /// </summary>
    /// <param name="segment">The segment to checkpoint.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The checkpoint that was cut, stored as pending.</returns>
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
