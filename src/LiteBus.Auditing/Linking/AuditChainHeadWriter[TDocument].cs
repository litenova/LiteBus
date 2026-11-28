using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     Appends audit documents to a hash chain segment with optimistic head contention.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         Both the inline capture paths and the after-commit linker write through this type, so
///         <see cref="AuditLink.InTransaction" />, <see cref="AuditLink.AtWrite" />, and
///         <see cref="AuditLink.AfterCommit" /> share one head discipline: read the head, assign sequences and
///         hashes, then append conditionally on the head version the read observed. A lost race throws
///         <see cref="AuditHeadConflictException" /> from the store, and the writer re-reads the winner and
///         retries, so two writers never share a sequence number.
///     </para>
///     <para>
///         New documents keep their list across retries, because they exist only in memory until the append
///         commits. Staged documents are re-read after a conflict, because the winning linker may have linked
///         some of them already.
///     </para>
/// </remarks>
internal sealed class AuditChainHeadWriter<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The durable chain operations the appends run against. Held for the life of the writer.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _store;

    /// <summary>
    ///     The segmenter that opens new segments and describes the identity stored on the head.
    ///     Held for the life of the writer.
    /// </summary>
    private readonly IAuditSegmenter<TDocument> _segmenter;

    /// <summary>
    ///     The registry that resolves the scheme new records are written with, by scheme name.
    ///     Held for the life of the writer.
    /// </summary>
    private readonly IAuditHashSchemeRegistry _schemes;

    /// <summary>
    ///     The chain configuration carrying the scheme name new records are written with.
    ///     Held for the life of the writer.
    /// </summary>
    private readonly AuditHashChainOptions _chainOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditChainHeadWriter{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The durable chain operations the appends run against.</param>
    /// <param name="segmenter">The segmenter that opens new segments.</param>
    /// <param name="schemes">The registry that resolves the write scheme by name.</param>
    /// <param name="chainOptions">The chain configuration carrying the write scheme name.</param>
    internal AuditChainHeadWriter(
        IAuditChainStore<TDocument> store,
        IAuditSegmenter<TDocument> segmenter,
        IAuditHashSchemeRegistry schemes,
        AuditHashChainOptions chainOptions)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(schemes);
        ArgumentNullException.ThrowIfNull(chainOptions);

        _store = store;
        _segmenter = segmenter;
        _schemes = schemes;
        _chainOptions = chainOptions;
    }

    /// <summary>
    ///     Appends new documents to a segment, assigning their chain positions under head contention.
    /// </summary>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="documents">The new documents without chain positions, in the order they link.</param>
    /// <param name="cancellationToken">The token used to cancel the append.</param>
    /// <returns>The head after the append committed.</returns>
    /// <remarks>
    ///     The <see cref="AuditLink.InTransaction" /> and <see cref="AuditLink.AtWrite" /> capture paths call
    ///     this: the former inside the business transaction, the latter in the audit store's own transaction.
    ///     A lost append race retries against the winner with the same documents re-numbered.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="documents" /> holds no document.</exception>
    /// <exception cref="InvalidOperationException">The segment expired and no longer accepts records.</exception>
    internal async ValueTask<AuditSegmentHead> AppendNewAsync(
        string segment,
        IReadOnlyList<TDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(documents));
        }

        foreach (var document in documents)
        {
            if (document is null)
            {
                throw new ArgumentException("The documents must not contain a null entry.", nameof(documents));
            }
        }

        var scheme = _schemes.Resolve<TDocument>(_chainOptions.SchemeName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var head = await _store.ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

            ThrowWhenExpired(segment, head);

            var assignment = await AssignPositionsAsync(
                    scheme,
                    segment,
                    head,
                    documents,
                    cancellationToken)
                .ConfigureAwait(false);

            var expected = head ?? NewEmptyHead(assignment.Info);
            var updated = expected with
            {
                Info = assignment.Info,
                LastSequence = assignment.LastSequence,
                LastHash = assignment.LastHash,
                Count = expected.Count + assignment.Linked.Count,
                Version = expected.Version + 1,
            };

            try
            {
                await _store
                    .AppendAsync(segment, expected, assignment.Linked, updated, cancellationToken)
                    .ConfigureAwait(false);

                return updated;
            }
            catch (AuditHeadConflictException)
            {
                // Another writer committed first; the next pass re-reads the winner and re-numbers the same documents.
            }
        }
    }

    /// <summary>
    ///     Links the staged documents of a segment in commit order.
    /// </summary>
    /// <param name="segment">The segment to link.</param>
    /// <param name="olderThan">
    ///     Only documents staged longer ago than this, or <see langword="null" /> for every staged document.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the linking.</param>
    /// <returns>The number of staged documents linked, excluding any genesis document.</returns>
    /// <remarks>
    ///     The <see cref="AuditLink.AfterCommit" /> path calls this right after commit with no age limit, and
    ///     the background sweep calls it with the sweep age for crash leftovers. A lost race re-reads the
    ///     staged documents, because the winner may have linked some of them already.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment expired and no longer accepts records.</exception>
    internal async ValueTask<int> LinkStagedAsync(
        string segment,
        TimeSpan? olderThan,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        var scheme = _schemes.Resolve<TDocument>(_chainOptions.SchemeName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var head = await _store.ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

            ThrowWhenExpired(segment, head);

            var pending = await CollectUnlinkedAsync(segment, olderThan, cancellationToken).ConfigureAwait(false);

            if (pending.Count == 0)
            {
                return 0;
            }

            var assignment = await AssignPositionsAsync(
                    scheme,
                    segment,
                    head,
                    pending,
                    cancellationToken)
                .ConfigureAwait(false);

            var expected = head ?? NewEmptyHead(assignment.Info);
            var updated = expected with
            {
                Info = assignment.Info,
                LastSequence = assignment.LastSequence,
                LastHash = assignment.LastHash,
                Count = expected.Count + assignment.Linked.Count,
                Version = expected.Version + 1,
            };

            try
            {
                await _store
                    .AppendAsync(segment, expected, assignment.Linked, updated, cancellationToken)
                    .ConfigureAwait(false);

                return pending.Count;
            }
            catch (AuditHeadConflictException)
            {
                // Another linker committed first; the next pass re-reads the staged documents and the winner.
            }
        }
    }

    /// <summary>
    ///     Throws when the segment expired and is closed to new writes.
    /// </summary>
    /// <param name="segment">The segment named in the error.</param>
    /// <param name="head">The head read from the store, or <see langword="null" /> when none exists.</param>
    /// <exception cref="InvalidOperationException">The segment expired.</exception>
    private static void ThrowWhenExpired(string segment, AuditSegmentHead? head)
    {
        if (head is not null && head.Expired)
        {
            throw new InvalidOperationException(
                $"The audit segment '{segment}' has expired and no longer accepts records.");
        }
    }

    /// <summary>
    ///     Builds the empty expected head a first append asserts when the store holds no head yet.
    /// </summary>
    /// <param name="info">The segment identity the append stores on the created head.</param>
    /// <returns>An empty head at version zero.</returns>
    private static AuditSegmentHead NewEmptyHead(AuditSegmentInfo info)
    {
        return new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
    }

    /// <summary>
    ///     Reads the staged documents of a segment in commit order.
    /// </summary>
    /// <param name="segment">The segment to read.</param>
    /// <param name="olderThan">
    ///     Only documents staged longer ago than this, or <see langword="null" /> for every staged document.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The staged documents in commit order.</returns>
    private async ValueTask<List<TDocument>> CollectUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        CancellationToken cancellationToken)
    {
        List<TDocument> pending = [];

        await foreach (var document in _store.ListUnlinkedAsync(segment, olderThan, cancellationToken).ConfigureAwait(false))
        {
            pending.Add(document);
        }

        return pending;
    }

    /// <summary>
    ///     Assigns chain positions and hashes to documents against the head just read.
    /// </summary>
    /// <param name="scheme">The scheme new records are hashed with.</param>
    /// <param name="segment">The segment the documents link into.</param>
    /// <param name="head">The head the read observed, or <see langword="null" /> when none exists.</param>
    /// <param name="documents">The documents to number, in the order they link.</param>
    /// <param name="cancellationToken">The token used to cancel the hashing.</param>
    /// <returns>The linked documents with the identity and tail they imply.</returns>
    /// <remarks>
    ///     A segment with no linked documents opens with a genesis document when the segmenter creates one.
    ///     A head that holds no linked documents carries the placeholder identity its staging wrote, so the
    ///     segmenter describes the stored identity again; otherwise the stored identity is kept.
    /// </remarks>
    private async ValueTask<ChainAssignment> AssignPositionsAsync(
        AuditHashScheme<TDocument> scheme,
        string segment,
        AuditSegmentHead? head,
        IReadOnlyList<TDocument> documents,
        CancellationToken cancellationToken)
    {
        var opensSegment = head is null || (head.Count == 0 && head.LastSequence == 0);
        var info = opensSegment
            ? _segmenter.DescribeSegment(segment, documents[0])
            : head!.Info;

        List<TDocument> linked = [];
        var previousHash = head?.LastHash;
        var sequence = (head?.LastSequence ?? 0) + 1;

        if (opensSegment)
        {
            var genesis = _segmenter.CreateGenesis(segment, documents[0]);

            if (genesis is not null)
            {
                genesis.Segment = segment;
                genesis.Sequence = sequence;
                genesis.PreviousHash = null;
                genesis.HashScheme = scheme.Name;
                genesis.Hash = await HashDocumentAsync(
                        scheme,
                        genesis,
                        segment,
                        sequence,
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);

                linked.Add(genesis);
                previousHash = genesis.Hash;
                sequence++;
            }
        }

        foreach (var document in documents)
        {
            document.Segment = segment;
            document.Sequence = sequence;
            document.PreviousHash = previousHash;
            document.HashScheme = scheme.Name;
            document.Hash = await HashDocumentAsync(
                    scheme,
                    document,
                    segment,
                    sequence,
                    previousHash,
                    cancellationToken)
                .ConfigureAwait(false);

            linked.Add(document);
            previousHash = document.Hash;
            sequence++;
        }

        return new ChainAssignment(info, linked, sequence - 1, previousHash);
    }

    /// <summary>
    ///     Hashes one document with a scheme: canonical form, link rule, then the hasher.
    /// </summary>
    /// <param name="scheme">The scheme the hash is computed with.</param>
    /// <param name="document">The document with its chain fields assigned, except the hash itself.</param>
    /// <param name="segment">The segment the document links into.</param>
    /// <param name="sequence">The position of the document in the segment.</param>
    /// <param name="previousHash">The hash of the previous document, or <see langword="null" /> for the first one.</param>
    /// <param name="cancellationToken">The token used to cancel the hash.</param>
    /// <returns>The hash stored on the document.</returns>
    private static async ValueTask<string> HashDocumentAsync(
        AuditHashScheme<TDocument> scheme,
        TDocument document,
        string segment,
        long sequence,
        string? previousHash,
        CancellationToken cancellationToken)
    {
        var canonical = scheme.Canonicalizer.Canonicalize(document);
        var linked = scheme.LinkRule.Link(new AuditLinkInput(segment, sequence, previousHash, canonical));

        return await scheme.Hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     The documents one head assignment numbered, with the identity and tail they imply.
    /// </summary>
    /// <param name="Info">The segment identity the append stores on the head.</param>
    /// <param name="Linked">The linked documents in ascending sequence order, genesis first when one was created.</param>
    /// <param name="LastSequence">The sequence of the last linked document.</param>
    /// <param name="LastHash">The hash of the last linked document.</param>
    private sealed record ChainAssignment(
        AuditSegmentInfo Info,
        IReadOnlyList<TDocument> Linked,
        long LastSequence,
        string? LastHash);
}
