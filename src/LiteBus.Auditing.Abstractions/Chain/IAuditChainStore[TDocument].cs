using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The durable operations an audit chain engine needs from a store.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         One engine runs on every store through this port: the head with its version, conditional appends, staging
///         of unlinked documents in the business transaction, reads in commit and sequence order, heads by filter,
///         checkpoints with publish state, and deletion by filter. Each store implements the port in its own adapter.
///     </para>
///     <para>
///         An unlinked document is one staged with the business change whose chain position is not assigned yet: its
///         <see cref="IAuditChainDocument.Sequence" /> is zero and its hash fields are <see langword="null" />. The
///         store returns unlinked documents in the order they committed.
///     </para>
/// </remarks>
public interface IAuditChainStore<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Reads the head of a segment.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The head, or <see langword="null" /> when the segment has no head yet.</returns>
    ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken);

    /// <summary>
    ///     Appends linked documents and moves the head, asserting the expected head version.
    /// </summary>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="expected">The head version the append asserts.</param>
    /// <param name="documents">The linked documents to append, in ascending sequence order.</param>
    /// <param name="updated">The head after the append.</param>
    /// <param name="cancellationToken">The token used to cancel the append.</param>
    /// <returns>A task representing the asynchronous append.</returns>
    /// <remarks>
    ///     For a segment with no head, <paramref name="expected" /> carries version zero and the store creates the
    ///     head from <paramref name="updated" />.
    /// </remarks>
    /// <exception cref="AuditHeadConflictException">The stored head version differs from <paramref name="expected" />.</exception>
    ValueTask AppendAsync(
        string segment,
        AuditSegmentHead expected,
        IReadOnlyList<TDocument> documents,
        AuditSegmentHead updated,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Stages an unlinked document in the current business transaction.
    /// </summary>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     The document commits or rolls back with the business change. The linker assigns its position after commit.
    /// </remarks>
    ValueTask StageAsync(string segment, TDocument document, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the unlinked documents of a segment in commit order.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="olderThan">
    ///     Only documents staged longer ago than this, or <see langword="null" /> for every unlinked document.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The unlinked documents in commit order.</returns>
    IAsyncEnumerable<TDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Reads linked documents of a segment by sequence page, in ascending sequence order.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="fromSequence">The first sequence to read, inclusive.</param>
    /// <param name="toSequence">The last sequence to read, inclusive, or <see langword="null" /> to read to the head.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The linked documents in ascending sequence order.</returns>
    IAsyncEnumerable<TDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Lists the heads matching a filter.
    /// </summary>
    /// <param name="filter">The segments to list.</param>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The matching heads.</returns>
    IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Stores a checkpoint with its publish state.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to store.</param>
    /// <param name="cancellationToken">The token used to cancel the store.</param>
    /// <returns>A task representing the asynchronous store.</returns>
    /// <remarks>
    ///     The checkpoint identity stays stable across publish retries, so anchors never diverge.
    /// </remarks>
    ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads one stored checkpoint by id.
    /// </summary>
    /// <param name="checkpointId">The checkpoint identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The checkpoint, or <see langword="null" /> when no checkpoint carries that id.</returns>
    ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken);

    /// <summary>
    ///     Lists the stored checkpoints still waiting for publication.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The checkpoints whose publish state is <see cref="CheckpointPublishState.Pending" />.</returns>
    IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
        CancellationToken cancellationToken);

    /// <summary>
    ///     Records one publish attempt for a stored checkpoint.
    /// </summary>
    /// <param name="checkpointId">The checkpoint identifier.</param>
    /// <param name="publishError">
    ///     The publish error, or <see langword="null" /> when the attempt published. A <see langword="null" />
    ///     error marks the checkpoint <see cref="CheckpointPublishState.Published" />; an error keeps it
    ///     <see cref="CheckpointPublishState.Pending" /> with the attempt count increased and the error recorded.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes the records, heads, and stored checkpoints matching a filter.
    /// </summary>
    /// <param name="filter">The segments to delete.</param>
    /// <param name="cancellationToken">The token used to cancel the deletion.</param>
    /// <returns>A task representing the asynchronous deletion.</returns>
    ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken);

    /// <summary>
    ///     Expires a segment: deletes every stored document of the segment and marks its head expired.
    /// </summary>
    /// <param name="segment">The segment to expire.</param>
    /// <param name="cancellationToken">The token used to cancel the expiry.</param>
    /// <returns>A task representing the asynchronous expiry.</returns>
    /// <remarks>
    ///     Retention, not erasure: the linked and unlinked documents go, while the head stays marked expired
    ///     and the stored checkpoints stay for verification. Callers cut a final checkpoint before expiring,
    ///     so the proof survives the records.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment has no head, holds no records, or has already expired.</exception>
    ValueTask ExpireAsync(string segment, CancellationToken cancellationToken);
}
