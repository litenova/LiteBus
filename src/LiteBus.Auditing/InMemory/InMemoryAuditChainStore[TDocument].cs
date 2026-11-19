using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.InMemory;

/// <summary>
///     Thread-safe in-memory audit chain store for unit tests.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         Heads, linked documents, staged documents, and checkpoints live in process-local dictionaries under
///         one lock, so assertions read committed state without a database. Staging writes a placeholder head
///         for a segment that has none, so the linker sweep discovers staged-only segments through the stored
///         heads; the first append replaces the placeholder identity with the identity the segmenter describes.
///     </para>
///     <para>
///         Unlinked means staged but not appended: a staged document keeps its staged entry across failed link
///         attempts even after positions were assigned to it, because the linker mutates the staged instances
///         and appends them afterwards. Appends persist the updated head verbatim after asserting the expected
///         head version, and an empty document list moves the head alone, which is how the checkpoint lifecycle
///         advances a head past a cut checkpoint.
///     </para>
///     <para>
///         Staged documents are matched by reference: staging the same instance twice replaces its entry rather
///         than duplicating it. There is no transaction to join, so staged records are visible to every caller
///         immediately, like committed rows.
///     </para>
/// </remarks>
public sealed class InMemoryAuditChainStore<TDocument> : IAuditChainStore<TDocument>, ICheckpointSource
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The segments keyed by segment key, in ordinal order. Guarded by the store lock.
    /// </summary>
    private readonly Dictionary<string, SegmentState> _segments = new(StringComparer.Ordinal);

    /// <summary>
    ///     The stored checkpoints keyed by checkpoint identity. Guarded by the store lock.
    /// </summary>
    private readonly Dictionary<Guid, AuditCheckpoint> _checkpoints = [];

    /// <summary>
    ///     The lock that serializes every read and mutation.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The clock that stamps staged entries.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The insertion order assigned to the last staged entry. Guarded by the store lock.
    /// </summary>
    private long _stageOrder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryAuditChainStore{TDocument}" /> class.
    /// </summary>
    /// <param name="clock">The clock that stamps staged entries, or <see langword="null" /> for <see cref="TimeProvider.System" />.</param>
    public InMemoryAuditChainStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return new ValueTask<AuditSegmentHead?>(
                _segments.TryGetValue(segment, out var state) ? state.Head : null);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     An empty list moves the head alone. A stored head that expired rejects the append, because an
    ///     expired segment accepts no further records.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment expired, the updated head targets another segment, or a document belongs to another segment or breaks the order.</exception>
    public ValueTask AppendAsync(
        string segment,
        AuditSegmentHead expected,
        IReadOnlyList<TDocument> documents,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(updated);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(segment, updated.Info.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The updated audit head targets segment '{updated.Info.Key}' but the append targets '{segment}'.");
        }

        ThrowIfUnexpectedDocuments(segment, documents);

        lock (_sync)
        {
            var state = GetOrCreate(segment);

            if (state.Head is null)
            {
                if (expected.Version != 0)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' has no stored head but the append expected version {expected.Version}.");
                }
            }
            else
            {
                if (state.Head.Expired)
                {
                    throw new InvalidOperationException(
                        $"The audit segment '{segment}' has expired and no longer accepts records.");
                }

                if (state.Head.Version != expected.Version)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' changed between read and write: expected head version {expected.Version} but found {state.Head.Version}.");
                }
            }

            ThrowIfSequenceLinked(state, segment, documents);

            foreach (var document in documents)
            {
                RemoveStaged(state, document);
                state.Linked.Add(document);
            }

            state.Head = updated;

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A segment with no head gains a placeholder head carrying no linked documents, so the linker sweep
    ///     discovers the segment through the stored heads. Staging the same instance twice replaces its entry
    ///     rather than duplicating it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document already carries a chain position, or the segment expired.</exception>
    public ValueTask StageAsync(string segment, TDocument document, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (document.Sequence != 0)
        {
            throw new InvalidOperationException(
                $"The audit document already carries sequence {document.Sequence} and cannot be staged as unlinked.");
        }

        lock (_sync)
        {
            var state = GetOrCreate(segment);

            if (state.Head is { Expired: true })
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' has expired and no longer accepts records.");
            }

            var now = _clock.GetUtcNow();

            document.Segment = segment;
            RemoveStaged(state, document);
            state.Unlinked.Add(new StagedDocument(document, now, ++_stageOrder));

            state.Head ??= new AuditSegmentHead(
                new AuditSegmentInfo(segment, null, new Dictionary<string, string>(StringComparer.Ordinal), now),
                0,
                null,
                0,
                null,
                null,
                false,
                0);

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        List<TDocument> snapshot;

        lock (_sync)
        {
            if (!_segments.TryGetValue(segment, out var state))
            {
                snapshot = [];
            }
            else
            {
                var cutoff = olderThan.HasValue ? _clock.GetUtcNow() - olderThan.Value : (DateTimeOffset?) null;

                snapshot = state.Unlinked
                    .Where(entry => cutoff is null || entry.StagedAt <= cutoff.Value)
                    .OrderBy(entry => entry.StagedAt)
                    .ThenBy(entry => entry.Order)
                    .Select(entry => entry.Document)
                    .ToList();
            }
        }

        return new SnapshotAsyncEnumerable<TDocument>(snapshot, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Segments stream ordered by segment key.
    /// </remarks>
    public IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
        TimeSpan? olderThan,
        CancellationToken cancellationToken)
    {
        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        List<string> snapshot;

        lock (_sync)
        {
            var cutoff = olderThan.HasValue ? _clock.GetUtcNow() - olderThan.Value : (DateTimeOffset?) null;

            snapshot = _segments
                .Where(entry => entry.Value.Unlinked
                    .Any(staged => cutoff is null || staged.StagedAt <= cutoff.Value))
                .Select(entry => entry.Key)
                .OrderBy(segment => segment, StringComparer.Ordinal)
                .ToList();
        }

        return new SnapshotAsyncEnumerable<string>(snapshot, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentOutOfRangeException.ThrowIfNegative(fromSequence);

        if (toSequence.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(toSequence.Value);

            if (toSequence.Value < fromSequence)
            {
                throw new ArgumentException(
                    "The last sequence to read must not precede the first sequence.",
                    nameof(toSequence));
            }
        }

        List<TDocument> snapshot;

        lock (_sync)
        {
            if (!_segments.TryGetValue(segment, out var state))
            {
                snapshot = [];
            }
            else
            {
                var from = Math.Max(fromSequence, 1);

                snapshot = state.Linked
                    .Where(document => document.Sequence >= from &&
                        (toSequence is null || document.Sequence <= toSequence.Value))
                    .OrderBy(document => document.Sequence)
                    .ToList();
            }
        }

        return new SnapshotAsyncEnumerable<TDocument>(snapshot, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Heads stream ordered by segment key.
    /// </remarks>
    public IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        List<AuditSegmentHead> snapshot = [];

        lock (_sync)
        {
            foreach (var state in _segments.Values)
            {
                if (state.Head is { } head && MatchesFilter(head, filter, honorExpiry: true))
                {
                    snapshot.Add(head);
                }
            }

            snapshot.Sort(static (left, right) => string.CompareOrdinal(left.Info.Key, right.Info.Key));
        }

        return new SnapshotAsyncEnumerable<AuditSegmentHead>(snapshot, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Stored by checkpoint identity, so storing the same checkpoint twice overwrites rather than
    ///     duplicates: the identity stays stable across publish retries and anchors never diverge. The head
    ///     is untouched: the checkpoint lifecycle advances it past the cut with its own conditional append.
    /// </remarks>
    public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _checkpoints[checkpoint.Id] = checkpoint;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return new ValueTask<AuditCheckpoint?>(
                _checkpoints.TryGetValue(checkpointId, out var checkpoint) ? checkpoint : null);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Checkpoints stream oldest first, so the publish retrier retries the longest-waiting checkpoint next.
    /// </remarks>
    public IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(CancellationToken cancellationToken)
    {
        List<AuditCheckpoint> snapshot;

        lock (_sync)
        {
            snapshot = _checkpoints.Values
                .Where(checkpoint => checkpoint.PublishState == CheckpointPublishState.Pending)
                .OrderBy(checkpoint => checkpoint.CreatedAt)
                .ThenBy(checkpoint => checkpoint.Id)
                .ToList();
        }

        return new SnapshotAsyncEnumerable<AuditCheckpoint>(snapshot, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No stored checkpoint carries <paramref name="checkpointId" />.</exception>
    public ValueTask MarkPublishedAsync(
        Guid checkpointId,
        string? publishError,
        CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_checkpoints.TryGetValue(checkpointId, out var stored))
            {
                throw new InvalidOperationException(
                    $"No stored audit checkpoint carries the identifier '{checkpointId}'.");
            }

            _checkpoints[checkpointId] = publishError is null
                ? stored with
                {
                    PublishState = CheckpointPublishState.Published,
                    PublishAttempts = stored.PublishAttempts + 1,
                    LastPublishError = null,
                }
                : stored with
                {
                    PublishState = CheckpointPublishState.Pending,
                    PublishAttempts = stored.PublishAttempts + 1,
                    LastPublishError = publishError,
                };
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Erasure ignores whether a segment expired: an expired segment selected by the tenant, attribute, or
    ///     age predicates is erased with the rest, because erasure leaves nothing identifying. An empty filter
    ///     matches every segment. Every segment with staged documents carries a head, so matching heads covers
    ///     staged documents too.
    /// </remarks>
    public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            HashSet<string> matched = new(StringComparer.Ordinal);

            foreach (var entry in _segments)
            {
                if (entry.Value.Head is { } head && MatchesFilter(head, filter, honorExpiry: false))
                {
                    matched.Add(entry.Key);
                }
            }

            if (matched.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            foreach (var key in matched)
            {
                _segments.Remove(key);
            }

            List<Guid> checkpoints = [];

            foreach (var entry in _checkpoints)
            {
                if (matched.Contains(entry.Value.Segment))
                {
                    checkpoints.Add(entry.Key);
                }
            }

            foreach (var checkpointId in checkpoints)
            {
                _checkpoints.Remove(checkpointId);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Retention, not erasure: the linked and staged documents go, while the head stays marked expired
    ///     and the stored checkpoints stay for verification.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment has no head, holds no records, or has already expired.</exception>
    public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_segments.TryGetValue(segment, out var state) ||
                state.Head is null ||
                state.Head.LastSequence == 0)
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' holds no records, so it cannot expire.");
            }

            if (state.Head.Expired)
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' has expired already, so it cannot expire again.");
            }

            state.Linked.Clear();
            state.Unlinked.Clear();
            state.Head = state.Head with { Expired = true, Version = state.Head.Version + 1 };

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            AuditCheckpoint? latest = null;

            foreach (var checkpoint in _checkpoints.Values)
            {
                if (!string.Equals(checkpoint.Segment, segment, StringComparison.Ordinal))
                {
                    continue;
                }

                if (latest is null ||
                    checkpoint.Sequence > latest.Sequence ||
                    (checkpoint.Sequence == latest.Sequence && checkpoint.CreatedAt > latest.CreatedAt))
                {
                    latest = checkpoint;
                }
            }

            return new ValueTask<AuditCheckpoint?>(latest);
        }
    }

    /// <summary>
    ///     Returns the state of a segment, opening it when it has none.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The segment state. Never <see langword="null" />.</returns>
    /// <remarks>
    ///     Callers hold the store lock.
    /// </remarks>
    private SegmentState GetOrCreate(string segment)
    {
        if (!_segments.TryGetValue(segment, out var state))
        {
            state = new SegmentState();
            _segments.Add(segment, state);
        }

        return state;
    }

    /// <summary>
    ///     Drops every staged entry holding a document instance.
    /// </summary>
    /// <param name="state">The segment state.</param>
    /// <param name="document">The document instance whose staged entries are dropped.</param>
    /// <remarks>
    ///     Callers hold the store lock. Staged documents are matched by reference: the linker mutates the
    ///     staged instances and appends them afterwards, so the append moves those same instances to the
    ///     linked list.
    /// </remarks>
    private static void RemoveStaged(SegmentState state, TDocument document)
    {
        for (var index = state.Unlinked.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(state.Unlinked[index].Document, document))
            {
                state.Unlinked.RemoveAt(index);
            }
        }
    }

    /// <summary>
    ///     Fails when an append carries a document of another segment or a sequence that does not increase.
    /// </summary>
    /// <param name="segment">The segment the append targets.</param>
    /// <param name="documents">The documents the append carries.</param>
    /// <exception cref="InvalidOperationException">A document belongs to another segment or breaks the order.</exception>
    private static void ThrowIfUnexpectedDocuments(string segment, IReadOnlyList<TDocument> documents)
    {
        var previous = 0L;

        foreach (var document in documents)
        {
            if (!string.Equals(document.Segment, segment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The audit document carries segment '{document.Segment}' but the append targets '{segment}'.");
            }

            if (document.Sequence <= previous)
            {
                throw new InvalidOperationException(
                    $"The audit documents of segment '{segment}' must carry strictly increasing sequences, but sequence {document.Sequence} follows {previous}.");
            }

            previous = document.Sequence;
        }
    }

    /// <summary>
    ///     Fails when an append carries a sequence the segment already links.
    /// </summary>
    /// <param name="state">The segment state.</param>
    /// <param name="segment">The segment the append targets.</param>
    /// <param name="documents">The documents the append carries.</param>
    /// <exception cref="AuditHeadConflictException">A sequence is already linked.</exception>
    /// <remarks>
    ///     A lost race reports a conflict rather than corrupting the chain, so the caller re-reads the head
    ///     and retries with fresh sequences.
    /// </remarks>
    private static void ThrowIfSequenceLinked(SegmentState state, string segment, IReadOnlyList<TDocument> documents)
    {
        foreach (var document in documents)
        {
            foreach (var linked in state.Linked)
            {
                if (linked.Sequence == document.Sequence)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' already contains sequence {document.Sequence}; the appended " +
                        "documents must carry unlinked sequences.");
                }
            }
        }
    }

    /// <summary>
    ///     Decides whether a stored head matches a segment filter.
    /// </summary>
    /// <param name="head">The stored head.</param>
    /// <param name="filter">The segments to match.</param>
    /// <param name="honorExpiry">Whether an expired head matches only when the filter includes expired segments.</param>
    /// <returns><see langword="true" /> when the head matches every predicate the filter names.</returns>
    private static bool MatchesFilter(AuditSegmentHead head, AuditSegmentFilter filter, bool honorExpiry)
    {
        if (filter.TenantId is not null &&
            !string.Equals(head.Info.TenantId, filter.TenantId, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.OpenedBefore.HasValue && head.Info.OpenedAt >= filter.OpenedBefore.Value)
        {
            return false;
        }

        if (honorExpiry && !filter.IncludeExpired && head.Expired)
        {
            return false;
        }

        return MatchesAttributes(head, filter.Attributes);
    }

    /// <summary>
    ///     Decides whether a stored head carries every attribute a filter names.
    /// </summary>
    /// <param name="head">The stored head.</param>
    /// <param name="attributes">The required attributes, or <see langword="null" /> for any attributes.</param>
    /// <returns><see langword="true" /> when the head carries every required attribute value.</returns>
    private static bool MatchesAttributes(AuditSegmentHead head, IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return true;
        }

        foreach (var pair in attributes)
        {
            if (!head.Info.Attributes.TryGetValue(pair.Key, out var value) ||
                !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     The stored state of one segment: its head, linked documents, and staged documents.
    /// </summary>
    private sealed class SegmentState
    {
        /// <summary>
        ///     Gets or sets the segment head, or <see langword="null" /> when nothing was ever staged or appended.
        /// </summary>
        public AuditSegmentHead? Head { get; set; }

        /// <summary>
        ///     Gets the linked documents in the order they appended.
        /// </summary>
        public List<TDocument> Linked { get; } = [];

        /// <summary>
        ///     Gets the staged documents in the order they staged.
        /// </summary>
        public List<StagedDocument> Unlinked { get; } = [];
    }

    /// <summary>
    ///     A document staged for linking, with when and in which order it staged.
    /// </summary>
    /// <param name="Document">The staged document instance.</param>
    /// <param name="StagedAt">When the document staged.</param>
    /// <param name="Order">The insertion order the document staged in.</param>
    private sealed record StagedDocument(TDocument Document, DateTimeOffset StagedAt, long Order);

    /// <summary>
    ///     An async sequence over a snapshot taken under the store lock.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    private sealed class SnapshotAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        /// <summary>
        ///     The snapshot to yield. Fixed at construction.
        /// </summary>
        private readonly IReadOnlyList<T> _items;

        /// <summary>
        ///     The token the read was called with. Observed between items.
        /// </summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SnapshotAsyncEnumerable{T}" /> class.
        /// </summary>
        /// <param name="items">The snapshot to yield.</param>
        /// <param name="cancellationToken">The token the read was called with.</param>
        public SnapshotAsyncEnumerable(IReadOnlyList<T> items, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(items);

            _items = items;
            _cancellationToken = cancellationToken;
        }

        /// <inheritdoc />
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new SnapshotAsyncEnumerator<T>(_items, _cancellationToken, cancellationToken);
        }
    }

    /// <summary>
    ///     Enumerates a snapshot taken under the store lock.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    private sealed class SnapshotAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        /// <summary>
        ///     The snapshot to yield. Fixed at construction.
        /// </summary>
        private readonly IReadOnlyList<T> _items;

        /// <summary>
        ///     The token the read was called with. Observed between items.
        /// </summary>
        private readonly CancellationToken _readToken;

        /// <summary>
        ///     The token the enumeration runs with. Observed between items.
        /// </summary>
        private readonly CancellationToken _enumerationToken;

        /// <summary>
        ///     The index of the current item, or -1 before the first move.
        /// </summary>
        private int _index = -1;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SnapshotAsyncEnumerator{T}" /> class.
        /// </summary>
        /// <param name="items">The snapshot to yield.</param>
        /// <param name="readToken">The token the read was called with.</param>
        /// <param name="enumerationToken">The token the enumeration runs with.</param>
        public SnapshotAsyncEnumerator(
            IReadOnlyList<T> items,
            CancellationToken readToken,
            CancellationToken enumerationToken)
        {
            ArgumentNullException.ThrowIfNull(items);

            _items = items;
            _readToken = readToken;
            _enumerationToken = enumerationToken;
        }

        /// <inheritdoc />
        public T Current => _items[_index];

        /// <inheritdoc />
        public ValueTask<bool> MoveNextAsync()
        {
            _readToken.ThrowIfCancellationRequested();
            _enumerationToken.ThrowIfCancellationRequested();

            _index++;

            return new ValueTask<bool>(_index < _items.Count);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
