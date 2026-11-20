using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     Stores hash-chained audit documents as Marten documents: segment heads with optimistic concurrency, staged
///     documents that join the business session, linked documents by sequence page, and checkpoints with publish
///     state.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         The stored document type must carry a readable <c>Id</c> property, following the Marten identity
///         convention, so staged documents can be ordered and erased. The constructor fails when it does not.
///     </para>
///     <para>
///         The store holds no session. Staging joins the business unit of work through the session the caller
///         passes: <see cref="StageAsync" /> stores the document and its <see cref="MartenAuditUnlinkedEntry" /> on
///         that session without saving, so both commit or roll back with the handler change. Every other operation
///         opens its own short session from <see cref="IDocumentStore" />, so one instance serves every caller and
///         the auditing module registers it as a singleton. The application registers Marten itself with
///         <c>AddMarten</c>, which supplies both the scoped session and the document store; LiteBus does not
///         register Marten.
///     </para>
///     <para>
///         Appends serialize on the segment head. The store checks the expected head version explicitly and relies
///         on Marten optimistic concurrency as the backstop for the race between the check and the commit; either
///         way a lost race reports <see cref="AuditHeadConflictException" /> and the caller retries against the
///         winner.
///     </para>
/// </remarks>
public sealed class MartenAuditChainStore<TDocument> : IAuditChainStore<TDocument>, ICheckpointSource
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The number of documents one segment page reads before yielding them.
    /// </summary>
    private const int SegmentPageSize = 1000;

    /// <summary>
    ///     Gets the document store that opens the short sessions every operation except staging runs on.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Gets the clock that stamps staged entries.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the readable identity property of the stored document type.
    /// </summary>
    private readonly PropertyInfo _identityProperty;

    /// <summary>
    ///     Gets the readable tenant property of the stored document type, or <see langword="null" /> when the
    ///     type carries none.
    /// </summary>
    private readonly PropertyInfo? _tenantProperty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChainStore{TDocument}" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="clock">The clock that stamps staged entries, or <see langword="null" /> for <see cref="TimeProvider.System" />.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TDocument" /> carries no readable <c>Id</c> property.</exception>
    public MartenAuditChainStore(
        IDocumentStore documentStore,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(documentStore);

        _documentStore = documentStore;
        _clock = clock ?? TimeProvider.System;
        _identityProperty = ResolveIdentityProperty();
        _tenantProperty = ResolveTenantProperty();
    }

    /// <inheritdoc />
    public async ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var head = await session
                .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                .ConfigureAwait(false);

            return head?.ToAuditSegmentHead();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The appended documents must belong to <paramref name="segment" /> and carry strictly increasing
    ///     sequences; an empty list moves the head only. A stored head that expired rejects the append, because
    ///     an expired segment accepts no further records.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment expired, or a document belongs to another segment.</exception>
    public async ValueTask AppendAsync(
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

        if (!string.Equals(segment, updated.Info.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The updated audit head targets segment '{updated.Info.Key}' but the append targets '{segment}'.");
        }

        ThrowIfUnexpectedDocuments(segment, documents);

        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var head = await session
                .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                .ConfigureAwait(false);

            if (head is null)
            {
                if (expected.Version != 0)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' has no stored head but the append expected version {expected.Version}.");
                }

                session.Insert(MartenAuditSegmentHead.FromAuditSegmentHead(updated));
            }
            else
            {
                if (head.Expired)
                {
                    throw new InvalidOperationException(
                        $"The audit segment '{segment}' has expired and no longer accepts records.");
                }

                if (head.Version != expected.Version)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' changed between read and write: expected head version {expected.Version} but found {head.Version}.");
                }

                // The loaded head is updated in place, never replaced: the update asserts the version captured
                // at load, which is the check that serializes concurrent appends.
                head.Apply(updated);
                session.Update(head);
            }

            foreach (var document in documents)
            {
                session.Store(document);
            }

            foreach (var document in documents)
            {
                var key = DocumentKey(document);

                session.DeleteWhere<MartenAuditUnlinkedEntry>(
                    entry => entry.Segment == segment && entry.DocumentKey == key);
            }

            try
            {
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsHeadWriteConflict(exception))
            {
                throw new AuditHeadConflictException(
                    $"The audit segment '{segment}' changed between read and write: another writer committed first.",
                    exception);
            }
        }
    }

    /// <summary>
    ///     Stages an unlinked document on the business session without saving it.
    /// </summary>
    /// <param name="session">The scoped business session the handler change is written on.</param>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     Staged, not saved: the unit-of-work completion handler commits the session, so the document and its
    ///     staged entry commit in the same <c>SaveChangesAsync</c> as the handler change, and the linker assigns the
    ///     document its position after commit. Staging the same document twice replaces its entry rather than
    ///     duplicating it, so a redelivered record overwrites rather than duplicates.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    public ValueTask StageAsync(
        IDocumentSession session,
        string segment,
        TDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        cancellationToken.ThrowIfCancellationRequested();

        var key = DocumentKey(document);

        if (document.Sequence != 0)
        {
            throw new InvalidOperationException(
                $"The audit document '{key}' already carries sequence {document.Sequence} and cannot be staged as unlinked.");
        }

        document.Segment = segment;
        session.Store(document);
        session.DeleteWhere<MartenAuditUnlinkedEntry>(
            entry => entry.Segment == segment && entry.DocumentKey == key);
        session.Store(new MartenAuditUnlinkedEntry
        {
            Id = Guid.NewGuid(),
            Segment = segment,
            DocumentKey = key,
            TenantId = DocumentTenant(document),
            StagedAt = _clock.GetUtcNow()
        });

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Stages an unlinked document on its own session and saves it immediately, outside the business
    ///     transaction.
    /// </summary>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     The unit of work rolls the handler session back on a denial, failure, cancellation, or invalid
    ///     message, so staging such a record on that session would lose it. This method writes the document and
    ///     its staged entry on a short session instead, so the record survives the rollback it describes and the
    ///     linker still positions it in commit order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    public async ValueTask StageOutOfBandAsync(string segment, TDocument document, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        var session = OpenOutOfBandSession();

        await using (session.ConfigureAwait(false))
        {
            await StageOutOfBandAsync(session, segment, document, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Opens a short session for the out-of-band path, outside the business transaction.
    /// </summary>
    /// <returns>The session; the caller disposes it.</returns>
    internal IDocumentSession OpenOutOfBandSession()
    {
        return _documentStore.LightweightSession(IsolationLevel.ReadCommitted);
    }

    /// <summary>
    ///     Stages an unlinked document on a short out-of-band session and saves the session, so anything else written
    ///     on it, such as a row the record mapper created, commits with the document.
    /// </summary>
    /// <param name="session">The out-of-band session from <see cref="OpenOutOfBandSession" />.</param>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    internal async ValueTask StageOutOfBandAsync(
        IDocumentSession session,
        string segment,
        TDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        var key = DocumentKey(document);

        if (document.Sequence != 0)
        {
            throw new InvalidOperationException(
                $"The audit document '{key}' already carries sequence {document.Sequence} and cannot be staged as unlinked.");
        }

        document.Segment = segment;
        session.Store(document);
        session.DeleteWhere<MartenAuditUnlinkedEntry>(
            entry => entry.Segment == segment && entry.DocumentKey == key);
        session.Store(new MartenAuditUnlinkedEntry
        {
            Id = Guid.NewGuid(),
            Segment = segment,
            DocumentKey = key,
            TenantId = DocumentTenant(document),
            StagedAt = _clock.GetUtcNow()
        });

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Documents follow their staged entries in commit order. A document whose entry is missing, for example
    ///     one written before entries existed, is still returned, after the entries in storage order: the store
    ///     never strands evidence it cannot date. An entry whose document is gone is skipped. When
    ///     <paramref name="olderThan" /> is set, a document whose entry is younger than the cutoff is excluded
    ///     rather than mistaken for a document without an entry.
    /// </remarks>
    public async IAsyncEnumerable<TDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var cutoff = olderThan.HasValue ? _clock.GetUtcNow() - olderThan.Value : (DateTimeOffset?) null;
            var entriesQuery = session
                .Query<MartenAuditUnlinkedEntry>()
                .Where(entry => entry.Segment == segment);

            if (cutoff.HasValue)
            {
                var bound = cutoff.Value;
                entriesQuery = entriesQuery.Where(entry => entry.StagedAt <= bound);
            }

            var entries = await entriesQuery
                .OrderBy(entry => entry.StagedAt)
                .ThenBy(entry => entry.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            HashSet<string>? stagedKeys = null;

            if (cutoff.HasValue)
            {
                var keys = await session
                    .Query<MartenAuditUnlinkedEntry>()
                    .Where(entry => entry.Segment == segment)
                    .Select(entry => entry.DocumentKey)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                stagedKeys = new HashSet<string>(keys, StringComparer.Ordinal);
            }

            var documents = await session
                .Query<TDocument>()
                .Where(SegmentEquals(segment))
                .Where(SequenceEquals(0))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var byKey = new Dictionary<string, TDocument>(StringComparer.Ordinal);

            foreach (var document in documents)
            {
                byKey[DocumentKey(document)] = document;
            }

            var matched = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                if (byKey.TryGetValue(entry.DocumentKey, out var document))
                {
                    matched.Add(entry.DocumentKey);

                    yield return document;
                }
            }

            foreach (var document in documents)
            {
                var key = DocumentKey(document);

                if (matched.Contains(key))
                {
                    continue;
                }

                if (stagedKeys is not null && stagedKeys.Contains(key))
                {
                    // The entry exists but is younger than the cutoff, so the age filter excluded it.
                    continue;
                }

                yield return document;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Segments are read from the staged entries, so a segment whose records are all staged is listed even
    ///     though staging creates no head for it. Segments stream ordered by segment key.
    /// </remarks>
    public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            IQueryable<MartenAuditUnlinkedEntry> entriesQuery = session.Query<MartenAuditUnlinkedEntry>();

            if (olderThan.HasValue)
            {
                var bound = _clock.GetUtcNow() - olderThan.Value;
                entriesQuery = entriesQuery.Where(entry => entry.StagedAt <= bound);
            }

            var staged = await entriesQuery
                .Select(entry => entry.Segment)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var segments = staged
                .Distinct(StringComparer.Ordinal)
                .OrderBy(candidate => candidate, StringComparer.Ordinal);

            foreach (var segment in segments)
            {
                yield return segment;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Documents stream in sequence windows of <see cref="SegmentPageSize" />, so verifying a large segment
    ///     holds one page at a time. The cursor advances past the last sequence returned, which stays correct
    ///     across gaps.
    /// </remarks>
    public async IAsyncEnumerable<TDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var cursor = Math.Max(fromSequence, 1);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var windowEnd = cursor > long.MaxValue - SegmentPageSize
                    ? long.MaxValue
                    : cursor + SegmentPageSize - 1;
                var pageEnd = toSequence.HasValue ? Math.Min(toSequence.Value, windowEnd) : windowEnd;
                var page = await session
                    .Query<TDocument>()
                    .Where(SegmentEquals(segment))
                    .Where(SequenceAtLeast(cursor))
                    .Where(SequenceAtMost(pageEnd))
                    .OrderBy(SequenceKey())
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (page.Count == 0)
                {
                    yield break;
                }

                foreach (var document in page)
                {
                    yield return document;
                }

                var last = page[page.Count - 1].Sequence;

                if (toSequence.HasValue && last >= toSequence.Value)
                {
                    yield break;
                }

                if (last == long.MaxValue)
                {
                    yield break;
                }

                cursor = last + 1;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The tenant, opened-before, and expiry predicates run in the database; the attribute predicates run in
    ///     memory, because heads are few and attribute matching needs no index. Heads stream ordered by segment
    ///     key.
    /// </remarks>
    public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            IQueryable<MartenAuditSegmentHead> query = session.Query<MartenAuditSegmentHead>();

            if (filter.TenantId is not null)
            {
                var tenantId = filter.TenantId;
                query = query.Where(head => head.TenantId == tenantId);
            }

            if (filter.OpenedBefore.HasValue)
            {
                var openedBefore = filter.OpenedBefore.Value;
                query = query.Where(head => head.OpenedAt < openedBefore);
            }

            if (!filter.IncludeExpired)
            {
                query = query.Where(head => !head.Expired);
            }

            var heads = await query
                .OrderBy(head => head.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var head in heads)
            {
                if (MatchesAttributes(head, filter.Attributes))
                {
                    yield return head.ToAuditSegmentHead();
                }
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Stored by checkpoint identity, so storing the same checkpoint twice overwrites rather than
    ///     duplicates: the identity stays stable across publish retries and anchors never diverge.
    /// </remarks>
    public async ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            session.Store(MartenAuditCheckpoint.FromAuditCheckpoint(checkpoint));

            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var stored = await session
                .LoadAsync<MartenAuditCheckpoint>(checkpointId, cancellationToken)
                .ConfigureAwait(false);

            return stored?.ToAuditCheckpoint();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Checkpoints stream oldest first, so the publish retrier retries the longest-waiting checkpoint next.
    /// </remarks>
    public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var pending = CheckpointPublishState.Pending;
            var checkpoints = await session
                .Query<MartenAuditCheckpoint>()
                .Where(checkpoint => checkpoint.PublishState == pending)
                .OrderBy(checkpoint => checkpoint.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var checkpoint in checkpoints)
            {
                yield return checkpoint.ToAuditCheckpoint();
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No stored checkpoint carries <paramref name="checkpointId" />.</exception>
    public async ValueTask MarkPublishedAsync(
        Guid checkpointId,
        string? publishError,
        CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var stored = await session
                .LoadAsync<MartenAuditCheckpoint>(checkpointId, cancellationToken)
                .ConfigureAwait(false);

            if (stored is null)
            {
                throw new InvalidOperationException(
                    $"No stored audit checkpoint carries the identifier '{checkpointId}'.");
            }

            stored.PublishAttempts += 1;

            if (publishError is null)
            {
                stored.PublishState = CheckpointPublishState.Published;
                stored.LastPublishError = null;
            }
            else
            {
                stored.PublishState = CheckpointPublishState.Pending;
                stored.LastPublishError = publishError;
            }

            session.Store(stored);

            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Erasure ignores whether a segment expired: an expired segment selected by the tenant, attribute, or
    ///         age predicates is erased with the rest, because erasure leaves nothing identifying. An empty filter
    ///         matches every segment.
    ///     </para>
    ///     <para>
    ///         Segments are matched by their heads. A staged document whose segment has no head yet is matched by
    ///         its staged entry when the filter names the entry tenant, so erasure by tenant removes staged
    ///         documents even before the linker positions them.
    ///     </para>
    /// </remarks>
    public async ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matched = new HashSet<string>(StringComparer.Ordinal);
        var read = _documentStore.QuerySession();

        await using (read.ConfigureAwait(false))
        {
            var heads = await read
                .Query<MartenAuditSegmentHead>()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var head in heads)
            {
                if (MatchesFilterIgnoringExpiry(head, filter))
                {
                    matched.Add(head.Id);
                }
            }

            if (filter.TenantId is not null)
            {
                var tenantId = filter.TenantId;
                var staged = await read
                    .Query<MartenAuditUnlinkedEntry>()
                    .Where(entry => entry.TenantId == tenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entry in staged)
                {
                    matched.Add(entry.Segment);
                }
            }
        }

        if (matched.Count == 0)
        {
            return;
        }

        var write = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (write.ConfigureAwait(false))
        {
            foreach (var segment in matched)
            {
                var key = segment;

                write.DeleteWhere<TDocument>(SegmentEquals(key));
                write.DeleteWhere<MartenAuditSegmentHead>(head => head.Id == key);
                write.DeleteWhere<MartenAuditCheckpoint>(checkpoint => checkpoint.Segment == key);
                write.DeleteWhere<MartenAuditUnlinkedEntry>(entry => entry.Segment == key);
            }

            await write.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Retention, not erasure: the linked documents and the staged entries go in one session with the
    ///     head update, while the head stays marked expired and the stored checkpoints stay for verification.
    ///     A concurrent writer that commits first fails the save, which reports a head conflict.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment has no head, holds no records, or has already expired.</exception>
    public async ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var head = await session
                .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                .ConfigureAwait(false);

            if (head is null || head.LastSequence == 0)
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' holds no records, so it cannot expire.");
            }

            if (head.Expired)
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' has expired already, so it cannot expire again.");
            }

            session.DeleteWhere<TDocument>(SegmentEquals(segment));
            session.DeleteWhere<MartenAuditUnlinkedEntry>(entry => entry.Segment == segment);

            head.Expired = true;
            head.Version += 1;
            session.Update(head);

            try
            {
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsHeadWriteConflict(exception))
            {
                throw new AuditHeadConflictException(
                    $"The audit segment '{segment}' changed between read and write: another writer committed first.",
                    exception);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var checkpoints = await session
                .Query<MartenAuditCheckpoint>()
                .Where(checkpoint => checkpoint.Segment == segment)
                .OrderByDescending(checkpoint => checkpoint.Sequence)
                .ThenByDescending(checkpoint => checkpoint.CreatedAt)
                .Take(1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return checkpoints.Count == 0 ? null : checkpoints[0].ToAuditCheckpoint();
        }
    }

    /// <summary>
    ///     Returns the text identity of a stored document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The identity converted with the invariant culture.</returns>
    /// <exception cref="InvalidOperationException">The document carries no identity.</exception>
    private string DocumentKey(TDocument document)
    {
        var value = _identityProperty.GetValue(document);

        if (value is null)
        {
            throw new InvalidOperationException(
                $"The audit document of type '{typeof(TDocument).FullName}' carries no identity. Assign its 'Id' before staging it.");
        }

        var key = Convert.ToString(value, CultureInfo.InvariantCulture);

        return key ?? string.Empty;
    }

    /// <summary>
    ///     Returns the tenant a stored document belongs to, when the document type carries one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The tenant, or <see langword="null" /> when the type carries no tenant.</returns>
    private string? DocumentTenant(TDocument document)
    {
        if (_tenantProperty is null)
        {
            return null;
        }

        var tenant = _tenantProperty.GetValue(document) as string;

        return string.IsNullOrWhiteSpace(tenant) ? null : tenant;
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
    ///     Decides whether a stored head matches a segment filter, ignoring whether it expired.
    /// </summary>
    /// <param name="head">The stored head.</param>
    /// <param name="filter">The segments to match.</param>
    /// <returns><see langword="true" /> when the head matches every predicate the filter names.</returns>
    private static bool MatchesFilterIgnoringExpiry(MartenAuditSegmentHead head, AuditSegmentFilter filter)
    {
        if (filter.TenantId is not null &&
            !string.Equals(head.TenantId, filter.TenantId, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.OpenedBefore.HasValue && head.OpenedAt >= filter.OpenedBefore.Value)
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
    private static bool MatchesAttributes(
        MartenAuditSegmentHead head,
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return true;
        }

        foreach (var pair in attributes)
        {
            if (!head.Attributes.TryGetValue(pair.Key, out var value) ||
                !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Resolves the readable identity property of the stored document type.
    /// </summary>
    /// <returns>The <c>Id</c> property, matched case-insensitively.</returns>
    /// <exception cref="InvalidOperationException">The stored document type carries no readable <c>Id</c>.</exception>
    private static PropertyInfo ResolveIdentityProperty()
    {
        var property = typeof(TDocument).GetProperty(
            "Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.CanRead)
        {
            throw new InvalidOperationException(
                $"The audit document type '{typeof(TDocument).FullName}' carries no readable 'Id' property. " +
                "Marten audit documents need an identity the Marten convention recognizes, so staged documents can be ordered and erased.");
        }

        return property;
    }

    /// <summary>
    ///     Resolves the readable tenant property of the stored document type, when it has one.
    /// </summary>
    /// <returns>The string <c>TenantId</c> property, or <see langword="null" /> when the type carries none.</returns>
    private static PropertyInfo? ResolveTenantProperty()
    {
        var property = typeof(TDocument).GetProperty(
            "TenantId",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.CanRead || property.PropertyType != typeof(string))
        {
            return null;
        }

        return property;
    }

    /// <summary>
    ///     Builds a predicate matching the documents of one segment.
    /// </summary>
    /// <param name="segment">The segment to match.</param>
    /// <returns>The predicate over the stored document type.</returns>
    /// <remarks>
    ///     Built against the closed document type rather than written as a lambda over the type parameter, so the
    ///     query provider always sees the concrete segment member instead of the interface one.
    /// </remarks>
    private static Expression<Func<TDocument, bool>> SegmentEquals(string segment)
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Segment));
        var body = Expression.Equal(property, Expression.Constant(segment));

        return Expression.Lambda<Func<TDocument, bool>>(body, document);
    }

    /// <summary>
    ///     Builds a predicate matching the documents that carry one sequence.
    /// </summary>
    /// <param name="sequence">The sequence to match.</param>
    /// <returns>The predicate over the stored document type.</returns>
    private static Expression<Func<TDocument, bool>> SequenceEquals(long sequence)
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Sequence));
        var body = Expression.Equal(property, Expression.Constant(sequence));

        return Expression.Lambda<Func<TDocument, bool>>(body, document);
    }

    /// <summary>
    ///     Builds a predicate matching the documents at or after one sequence.
    /// </summary>
    /// <param name="sequence">The first sequence to match, inclusive.</param>
    /// <returns>The predicate over the stored document type.</returns>
    private static Expression<Func<TDocument, bool>> SequenceAtLeast(long sequence)
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Sequence));
        var body = Expression.GreaterThanOrEqual(property, Expression.Constant(sequence));

        return Expression.Lambda<Func<TDocument, bool>>(body, document);
    }

    /// <summary>
    ///     Builds a predicate matching the documents at or before one sequence.
    /// </summary>
    /// <param name="sequence">The last sequence to match, inclusive.</param>
    /// <returns>The predicate over the stored document type.</returns>
    private static Expression<Func<TDocument, bool>> SequenceAtMost(long sequence)
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Sequence));
        var body = Expression.LessThanOrEqual(property, Expression.Constant(sequence));

        return Expression.Lambda<Func<TDocument, bool>>(body, document);
    }

    /// <summary>
    ///     Builds the sequence key that orders the documents of one segment.
    /// </summary>
    /// <returns>The key over the stored document type.</returns>
    /// <remarks>
    ///     Built against the closed document type rather than written as a lambda over the type parameter, so the
    ///     query provider always sees the concrete sequence member instead of the interface one.
    /// </remarks>
    private static Expression<Func<TDocument, long>> SequenceKey()
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Sequence));

        return Expression.Lambda<Func<TDocument, long>>(property, document);
    }

    /// <summary>
    ///     Decides whether a failure a store session raised is a lost head write race worth reporting as a
    ///     conflict.
    /// </summary>
    /// <param name="exception">The failure a store session raised, whatever wrapper Marten put around it.</param>
    /// <returns><see langword="true" /> when the store reports a Marten concurrency or head creation conflict.</returns>
    private static bool IsHeadWriteConflict(Exception exception)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            if (current is ConcurrencyException or
                ConcurrentUpdateException or
                DocumentAlreadyExistsException)
            {
                return true;
            }

            if (current is PostgresException postgres && IsHeadWriteState(postgres.SqlState))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    /// <remarks>
    ///     A batched commit can report several failures in one <see cref="AggregateException" />, and its
    ///     <see cref="Exception.InnerException" /> is only the first of them, so walking that chain alone can miss
    ///     the storage error that matters.
    /// </remarks>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }

    /// <summary>
    ///     Decides whether a PostgreSQL state reports a lost head write race.
    /// </summary>
    /// <param name="sqlState">The five-character state the storage error reported.</param>
    /// <returns>
    ///     <see langword="true" /> for a unique violation (<c>23505</c>), a serialization failure (<c>40001</c>), or a
    ///     deadlock (<c>40P01</c>); otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsHeadWriteState(string sqlState)
    {
        return sqlState is "23505" or "40001" or "40P01";
    }
}
