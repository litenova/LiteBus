using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core audit chain store that implements the chain port over the scoped business
///     context.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         Every operation runs on the <see cref="IAuditDbContext" /> resolved from the mediation scope, so
///         staged documents commit or roll back with the business change. Staging adds without saving: the
///         unit of work commits. Every other operation saves, which joins the ambient transaction when one is
///         in flight and commits on its own otherwise.
///     </para>
///     <para>
///         Appends assert the expected head version against the stored head before writing, and the head
///         version is a concurrency token, so a lost race fails with
///         <see cref="AuditHeadConflictException" /> instead of sharing a sequence number. A failed append
///         detaches its head and added documents, so the engine can retry on the same scope. A head creation
///         race fails with a unique violation on the segment key, which the store also reports as a head
///         conflict through <c>EfCoreUniqueViolation</c>.
///     </para>
///     <para>
///         Segment reads stream without tracking, so verification pages a large segment with bounded memory.
///         Unlinked reads stay tracked, because the linker assigns chain positions on the returned instances.
///     </para>
///     <para>
///         The in-memory provider enforces no concurrency token, so appends on that provider serialize on a
///         process-wide lock. Relational providers rely on the token alone, which also serializes writers
///         across processes.
///     </para>
/// </remarks>
public sealed class EfCoreAuditChainStore<TDocument> : IAuditChainStore<TDocument>, ICheckpointSource
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Serializes audit appends on the in-memory provider across all store instances in the current
    ///     process.
    /// </summary>
    private static readonly SemaphoreSlim InMemoryAppendLock = new(1, 1);

    /// <summary>
    ///     The audit database context of the current scope that every operation runs on.
    /// </summary>
    private readonly IAuditDbContext _auditContext;

    /// <summary>
    ///     The clock that stamps staged documents and opened segments.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The factory that opens the isolated contexts out-of-band staging saves on, or <see langword="null" />
    ///     when the application registered none.
    /// </summary>
    private readonly IAuditDbContextFactory? _dbContextFactory;

    /// <summary>
    ///     The readable tenant property of the stored document type, or <see langword="null" /> when the type
    ///     carries none.
    /// </summary>
    private readonly PropertyInfo? _tenantProperty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditChainStore{TDocument}" /> class.
    /// </summary>
    /// <param name="auditContext">The audit database context of the current scope.</param>
    /// <param name="timeProvider">
    ///     The clock that stamps staged documents and opened segments, or <see langword="null" /> for
    ///     <see cref="TimeProvider.System" />.
    /// </param>
    /// <param name="dbContextFactory">
    ///     The factory that opens the isolated contexts <see cref="StageOutOfBandAsync" /> saves on, or
    ///     <see langword="null" /> when out-of-band staging is not used.
    /// </param>
    public EfCoreAuditChainStore(
        IAuditDbContext auditContext,
        TimeProvider? timeProvider = null,
        IAuditDbContextFactory? dbContextFactory = null)
    {
        ArgumentNullException.ThrowIfNull(auditContext);

        _auditContext = auditContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dbContextFactory = dbContextFactory;
        _tenantProperty = ResolveTenantProperty();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The store is its own checkpoint source: the latest checkpoint is the highest sequence cut for the
    ///     segment, so verification compares the chain against the store first without another registration.
    /// </remarks>
    public async ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Reading the latest audit checkpoint requires a DbContext-backed audit database context.");
        }

        var entity = await dbContext.Set<AuditCheckpointEntity>().AsNoTracking()
            .Where(checkpoint => checkpoint.Segment == segment)
            .OrderByDescending(checkpoint => checkpoint.Sequence)
            .ThenByDescending(checkpoint => checkpoint.CreatedAt)
            .ThenByDescending(checkpoint => checkpoint.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapCheckpoint(entity);
    }

    /// <inheritdoc />
    public async ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Reading audit segment heads requires a DbContext-backed audit database context.");
        }

        var entity = await dbContext.Set<AuditSegmentHeadEntity>().AsNoTracking()
            .SingleOrDefaultAsync(head => head.Segment == segment, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapHead(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Documents that are already tracked, such as staged documents the linker just numbered, flush
    ///         with the head in one save. Detached documents, such as a genesis document or a record linked
    ///         where it is written, are added first. An empty document list moves the head only, which is how
    ///         the checkpoint lifecycle records a cut.
    ///     </para>
    ///     <para>
    ///         The segment parameter is authoritative: a document without a segment is assigned to it, and a
    ///         document carrying another segment fails loudly instead of crossing segments.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The segment expired, a document belongs to another segment or breaks the sequence order, or
    ///     <paramref name="updated" /> targets another segment.
    /// </exception>
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

        for (var index = 0; index < documents.Count; index++)
        {
            if (documents[index] is null)
            {
                throw new ArgumentException("The documents must not contain a null entry.", nameof(documents));
            }
        }

        if (!string.Equals(segment, updated.Info.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The updated audit head targets segment '{updated.Info.Key}' but the append targets '{segment}'.");
        }

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Appending audit documents requires a DbContext-backed audit database context.");
        }

        foreach (var document in documents)
        {
            AssignSegment(segment, document);
        }

        ThrowIfUnexpectedDocuments(segment, documents);

        if (EfCoreProviderResolver.ResolveProviderName(dbContext.Database.ProviderName) == EfCoreStorageProvider.InMemory)
        {
            await InMemoryAppendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await AppendCoreAsync(dbContext, segment, expected, documents, updated, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                InMemoryAppendLock.Release();
            }

            return;
        }

        await AppendCoreAsync(dbContext, segment, expected, documents, updated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Staging writes a placeholder head when the segment has none, so the background sweep discovers
    ///         the segment through the stored heads. The first link replaces the placeholder identity with the
    ///         identity the segmenter describes. The placeholder carries the staged document tenant when the
    ///         document type exposes one, so erasure by tenant removes staged-only segments with the rest; a
    ///         segment whose staged documents span tenants keeps the first tenant staged.
    ///     </para>
    ///     <para>
    ///         Staging the same document instance twice, or restaging a record that already stored, is a
    ///         no-op. Redelivery carries the same record identity, and the store holds one document per
    ///         primary key, so a redelivered record overwrites rather than duplicates. Documents with a
    ///         database-generated key cannot deduplicate, because no stable identity exists before the insert.
    ///         The placeholder and the document are added without saving, so both commit or roll back with
    ///         the business change.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    public async ValueTask StageAsync(string segment, TDocument document, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        if (document.Sequence != 0 || document.PreviousHash is not null || document.Hash is not null || document.HashScheme is not null)
        {
            throw new InvalidOperationException($"The audit document for segment '{segment}' already carries a chain position, so it cannot be staged as unlinked.");
        }

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Staging audit documents requires a DbContext-backed audit database context.");
        }

        AssignSegment(segment, document);
        ThrowIfDocumentNotConfigured(dbContext);
        await EnsurePlaceholderHeadAsync(dbContext, segment, DocumentTenant(document), cancellationToken).ConfigureAwait(false);

        if (dbContext.Entry(document).State != EntityState.Detached)
        {
            return;
        }

        if (await HasStoredDocumentAsync(dbContext, document, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        dbContext.Set<TDocument>().Add(document);
        dbContext.Entry(document).Property(AuditStoreJson.StagedAtPropertyName).CurrentValue = _timeProvider.GetUtcNow();
    }

    /// <summary>
    ///     Stages an unlinked document on an isolated context and saves it immediately, outside the business
    ///     transaction.
    /// </summary>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     The unit of work rolls the scoped context back on a denial, failure, cancellation, or invalid
    ///     message, so staging such a record on that context would lose it. This method stages the document
    ///     on a context from the configured <see cref="IAuditDbContextFactory" /> and saves it immediately
    ///     instead, so the record survives the rollback it describes and the linker still positions it in
    ///     commit order. A creation race on the placeholder head or the document retries once against the
    ///     winner, because staging is idempotent; the retry stages nothing new when the winner stored the
    ///     same document.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The document already carries a chain position, or no <see cref="IAuditDbContextFactory" /> is
    ///     configured.
    /// </exception>
    public async ValueTask StageOutOfBandAsync(string segment, TDocument document, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        await StageOutOfBandCoreAsync(_ => ValueTask.FromResult(document), _ => segment, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets the scoped business context the success path stages documents on.
    /// </summary>
    /// <exception cref="InvalidOperationException">The audit database context is not a <see cref="DbContext" />.</exception>
    internal DbContext BusinessContext => _auditContext as DbContext
        ?? throw new InvalidOperationException("Staging audit documents requires a DbContext-backed audit database context.");

    /// <summary>
    ///     Maps a record on an isolated context, stages the mapped document there, and saves the context, so anything
    ///     the mapper added to it commits with the document, outside the business transaction.
    /// </summary>
    /// <param name="map">Maps the record with the isolated context.</param>
    /// <param name="segmentOf">Returns the segment of the mapped document.</param>
    /// <param name="cancellationToken">The token used to cancel the mapping and the staging.</param>
    /// <returns>The segment the document was staged in.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The document already carries a chain position, or no <see cref="IAuditDbContextFactory" /> is configured.
    /// </exception>
    internal async ValueTask<string> StageOutOfBandCoreAsync(
        Func<DbContext, ValueTask<TDocument>> map,
        Func<TDocument, string> segmentOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(segmentOf);

        if (_dbContextFactory is null)
        {
            throw new InvalidOperationException(
                "Staging an audit document outside the business transaction requires an IAuditDbContextFactory. Register EfCoreAuditDbContextFactory<TContext> for the application database context so denials, failures, cancellations, and invalid messages persist when the business change rolls back.");
        }

        var created = _dbContextFactory.CreateDbContext();

        try
        {
            if (created is not DbContext isolated)
            {
                throw new InvalidOperationException("Staging audit documents outside the business transaction requires a DbContext-backed audit database context.");
            }

            var document = await map(isolated).ConfigureAwait(false);
            var segment = segmentOf(document);

            if (document.Sequence != 0 || document.PreviousHash is not null || document.Hash is not null || document.HashScheme is not null)
            {
                throw new InvalidOperationException($"The audit document for segment '{segment}' already carries a chain position, so it cannot be staged as unlinked.");
            }

            var outOfBand = new EfCoreAuditChainStore<TDocument>(created, _timeProvider);

            try
            {
                await outOfBand.StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
                await isolated.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (EfCoreUniqueViolation.IsUniqueViolation(exception))
            {
                // Only the audit rows raced; whatever the mapper added stays added and saves with the retry.
                DetachAddedAuditEntries(isolated);

                await outOfBand.StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
                await isolated.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return segment;
        }
        finally
        {
            if (created is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (created is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    ///     Detaches the audit document and segment head entries a failed save added, so a retry stages them again
    ///     against the row that won the race, and leaves every other added entry in place.
    /// </summary>
    /// <param name="dbContext">The context whose added audit entries are detached.</param>
    private static void DetachAddedAuditEntries(DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Added &&
                                     entry.Entity is TDocument or AuditSegmentHeadEntity)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The returned documents stay tracked, because the linker assigns their chain positions on these
    ///     instances before appending them. Commit order is staged-at order; documents staged in the same tick
    ///     link in database order, each exactly once.
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

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Listing unlinked audit documents requires a DbContext-backed audit database context.");
        }

        var query = dbContext.Set<TDocument>()
            .Where(document => document.Segment == segment && document.Sequence == 0);

        if (olderThan.HasValue)
        {
            var stagedBefore = _timeProvider.GetUtcNow() - olderThan.Value;
            query = query.Where(document => EF.Property<DateTimeOffset>(document, AuditStoreJson.StagedAtPropertyName) <= stagedBefore);
        }

        query = query.OrderBy(document => EF.Property<DateTimeOffset>(document, AuditStoreJson.StagedAtPropertyName));

        await foreach (var document in query.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return document;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Segments stream ordered by segment key.
    /// </remarks>
    public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Listing segments with unlinked audit documents requires a DbContext-backed audit database context.");
        }

        var query = dbContext.Set<TDocument>().AsNoTracking()
            .Where(document => document.Segment != null && document.Sequence == 0);

        if (olderThan.HasValue)
        {
            var stagedBefore = _timeProvider.GetUtcNow() - olderThan.Value;
            query = query.Where(document => EF.Property<DateTimeOffset>(document, AuditStoreJson.StagedAtPropertyName) <= stagedBefore);
        }

        var segments = await query
            .Select(document => document.Segment)
            .Distinct()
            .OrderBy(segment => segment)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var segment in segments)
        {
            if (segment is not null)
            {
                yield return segment;
            }
        }
    }

    /// <inheritdoc />
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

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Reading audit segments requires a DbContext-backed audit database context.");
        }

        var query = dbContext.Set<TDocument>().AsNoTracking()
            .Where(document => document.Segment == segment && document.Sequence > 0 && document.Sequence >= fromSequence);

        if (toSequence.HasValue)
        {
            var lastSequence = toSequence.Value;
            query = query.Where(document => document.Sequence <= lastSequence);
        }

        query = query.OrderBy(document => document.Sequence);

        await foreach (var document in query.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return document;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The tenant, opened-before, and expiry predicates run in the database. The attribute predicates run
    ///     in memory against the stored attribute JSON, because heads are few: one row per segment.
    /// </remarks>
    public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Listing audit segment heads requires a DbContext-backed audit database context.");
        }

        var heads = await ListMatchingHeadEntitiesAsync(dbContext, filter, honorExpiry: true, cancellationToken).ConfigureAwait(false);

        foreach (var head in heads)
        {
            yield return MapHead(head);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Storing the identical checkpoint twice is a no-op, because a cut that retries after its head moved
    ///     keeps the same identity. Storing a checkpoint whose identity matches a stored checkpoint with
    ///     different content fails loudly instead of overwriting evidence.
    /// </remarks>
    public async ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Storing audit checkpoints requires a DbContext-backed audit database context.");
        }

        var tracked = dbContext.Set<AuditCheckpointEntity>().Local.FirstOrDefault(entity => entity.Id == checkpoint.Id);

        if (tracked is not null)
        {
            if (CheckpointsEqual(tracked, checkpoint))
            {
                return;
            }

            throw new InvalidOperationException($"The stored audit checkpoint '{checkpoint.Id}' differs from the checkpoint with the same identity, so it cannot be stored again.");
        }

        var entity = MapCheckpointEntity(checkpoint);
        dbContext.Set<AuditCheckpointEntity>().Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            dbContext.Entry(entity).State = EntityState.Detached;

            AuditCheckpointEntity? stored = null;

            if (EfCoreUniqueViolation.IsUniqueViolation(exception))
            {
                stored = await ReadCheckpointEntityAsync(dbContext, checkpoint.Id, cancellationToken).ConfigureAwait(false);
            }

            if (stored is not null)
            {
                if (CheckpointsEqual(stored, checkpoint))
                {
                    return;
                }

                throw new InvalidOperationException($"The stored audit checkpoint '{checkpoint.Id}' differs from the checkpoint with the same identity, so it cannot be stored again.");
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Reading audit checkpoints requires a DbContext-backed audit database context.");
        }

        var entity = await ReadCheckpointEntityAsync(dbContext, checkpointId, cancellationToken).ConfigureAwait(false);

        return entity is null ? null : MapCheckpoint(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Checkpoints list oldest first, so the background publisher retries the longest waiting checkpoint
    ///     before newer ones.
    /// </remarks>
    public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Listing unpublished audit checkpoints requires a DbContext-backed audit database context.");
        }

        var query = dbContext.Set<AuditCheckpointEntity>().AsNoTracking()
            .Where(checkpoint => checkpoint.PublishState == CheckpointPublishState.Pending)
            .OrderBy(checkpoint => checkpoint.CreatedAt)
            .ThenBy(checkpoint => checkpoint.Id);

        await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return MapCheckpoint(entity);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Marking a checkpoint that no longer exists is a no-op: erasure deletes stored checkpoints, and a
    ///     publish retry that loses that race has nothing left to mark. A successful mark clears the last
    ///     publish error.
    /// </remarks>
    public async ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
    {
        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Recording audit checkpoint publication requires a DbContext-backed audit database context.");
        }

        var publishState = publishError is null ? CheckpointPublishState.Published : CheckpointPublishState.Pending;

        await EfCoreDurableStoreOperations.ExecuteUpdateOrTrackAsync(
            dbContext,
            () => dbContext.Set<AuditCheckpointEntity>()
                .Where(checkpoint => checkpoint.Id == checkpointId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(checkpoint => checkpoint.PublishState, publishState)
                        .SetProperty(checkpoint => checkpoint.PublishAttempts, checkpoint => checkpoint.PublishAttempts + 1)
                        .SetProperty(checkpoint => checkpoint.LastPublishError, publishError),
                    cancellationToken),
            dbContext.Set<AuditCheckpointEntity>().Where(checkpoint => checkpoint.Id == checkpointId),
            entity =>
            {
                entity.PublishAttempts++;

                if (publishError is null)
                {
                    entity.PublishState = CheckpointPublishState.Published;
                    entity.LastPublishError = null;
                }
                else
                {
                    entity.PublishState = CheckpointPublishState.Pending;
                    entity.LastPublishError = publishError;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Erasure selects segments through their heads with the same matching as
    ///         <see cref="ListHeadsAsync" />, except that it ignores whether a segment expired: an expired
    ///         segment selected by the tenant, attribute, or age predicates is erased with the rest, because
    ///         erasure leaves nothing identifying. Records staged but never linked erase with their segment,
    ///         because staging writes a head for every segment; a staged-only segment carries the tenant of
    ///         the document staged first.
    ///     </para>
    ///     <para>
    ///         Erased rows detach from the ambient scope, so a later save in the same scope cannot write them
    ///         back. Erasure races a concurrent link the way any delete races a concurrent write: the winner is
    ///         visible in the stored heads, and an operator serializes the two when both run at once.
    ///     </para>
    /// </remarks>
    public async ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Erasing audit segments requires a DbContext-backed audit database context.");
        }

        var heads = await ListMatchingHeadEntitiesAsync(dbContext, filter, honorExpiry: false, cancellationToken).ConfigureAwait(false);

        if (heads.Count == 0)
        {
            return;
        }

        var segments = heads.Select(head => head.Segment).ToList();
        var useSingleParameter = EfCoreCollectionParameters.UseSingleParameter(dbContext);

        await EfCoreDurableStoreOperations.DeleteMatchingAsync(
            dbContext,
            useSingleParameter
                ? dbContext.Set<TDocument>().Where(document => document.Segment != null && EF.Parameter(segments).Contains(document.Segment))
                : dbContext.Set<TDocument>().Where(document => document.Segment != null && segments.Contains(document.Segment)),
            cancellationToken).ConfigureAwait(false);

        await EfCoreDurableStoreOperations.DeleteMatchingAsync(
            dbContext,
            useSingleParameter
                ? dbContext.Set<AuditCheckpointEntity>().Where(checkpoint => EF.Parameter(segments).Contains(checkpoint.Segment))
                : dbContext.Set<AuditCheckpointEntity>().Where(checkpoint => segments.Contains(checkpoint.Segment)),
            cancellationToken).ConfigureAwait(false);

        await EfCoreDurableStoreOperations.DeleteMatchingAsync(
            dbContext,
            useSingleParameter
                ? dbContext.Set<AuditSegmentHeadEntity>().Where(head => EF.Parameter(segments).Contains(head.Segment))
                : dbContext.Set<AuditSegmentHeadEntity>().Where(head => segments.Contains(head.Segment)),
            cancellationToken).ConfigureAwait(false);

        PurgeTrackedScopeEntries(dbContext, segments);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Retention, not erasure: the linked and staged documents go, while the head stays marked expired
    ///     and the stored checkpoints stay for verification. The documents go before the head is marked, so
    ///     a retry after a lost race converges: the head still carries its sequence and the deletion runs
    ///     again as a no-op before the mark. Erased rows detach from the ambient scope, so a later save in
    ///     the same scope cannot write them back.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment has no head, holds no records, or has already expired.</exception>
    /// <exception cref="AuditHeadConflictException">A concurrent writer moved the head between the read and the mark.</exception>
    public async ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (_auditContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("Expiring audit segments requires a DbContext-backed audit database context.");
        }

        var head = await dbContext.Set<AuditSegmentHeadEntity>().FindAsync([segment], cancellationToken).ConfigureAwait(false);

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

        await EfCoreDurableStoreOperations.DeleteMatchingAsync(
            dbContext,
            dbContext.Set<TDocument>().Where(document => document.Segment == segment),
            cancellationToken).ConfigureAwait(false);

        head.Expired = true;
        head.Version += 1;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.Entry(head).State = EntityState.Detached;

            throw new AuditHeadConflictException(
                $"The audit segment '{segment}' changed between read and write: another writer committed first.",
                exception);
        }

        PurgeTrackedScopeEntries(dbContext, [segment]);
    }

    /// <summary>
    ///     Appends linked documents and moves the head on one database context, asserting the expected head
    ///     version.
    /// </summary>
    /// <param name="dbContext">The database context the append runs on.</param>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="expected">The head version the append asserts.</param>
    /// <param name="documents">The linked documents to append, in ascending sequence order.</param>
    /// <param name="updated">The head after the append.</param>
    /// <param name="cancellationToken">The token used to cancel the append.</param>
    /// <returns>A task representing the asynchronous append.</returns>
    /// <exception cref="AuditHeadConflictException">The stored head version differs from <paramref name="expected" />.</exception>
    private static async ValueTask AppendCoreAsync(
        DbContext dbContext,
        string segment,
        AuditSegmentHead expected,
        IReadOnlyList<TDocument> documents,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        var head = await dbContext.Set<AuditSegmentHeadEntity>().FindAsync([segment], cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfHeadConflict(segment, head, expected);

            head ??= dbContext.Set<AuditSegmentHeadEntity>().Add(new AuditSegmentHeadEntity { Segment = segment }).Entity;
            ApplyUpdatedHead(head, updated);

            foreach (var document in documents)
            {
                if (dbContext.Entry(document).State == EntityState.Detached)
                {
                    dbContext.Set<TDocument>().Add(document);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AuditHeadConflictException)
        {
            DetachFailedAppend(dbContext, head, documents);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            DetachFailedAppend(dbContext, head, documents);

            throw new AuditHeadConflictException(
                $"The audit segment '{segment}' changed between read and write: another writer committed first.",
                exception);
        }
        catch (DbUpdateException exception) when (EfCoreUniqueViolation.IsUniqueViolation(exception))
        {
            DetachFailedAppend(dbContext, head, documents);

            throw new AuditHeadConflictException(
                $"The audit segment '{segment}' changed between read and write: another writer committed first.",
                exception);
        }
    }

    /// <summary>
    ///     Fails an append when the stored head expired or moved past the expected version.
    /// </summary>
    /// <param name="segment">The segment the append targets.</param>
    /// <param name="head">The stored head, or <see langword="null" /> when the segment has none.</param>
    /// <param name="expected">The head version the append asserts.</param>
    /// <exception cref="InvalidOperationException">The segment expired and no longer accepts records.</exception>
    /// <exception cref="AuditHeadConflictException">The stored head version differs from <paramref name="expected" />.</exception>
    private static void ThrowIfHeadConflict(string segment, AuditSegmentHeadEntity? head, AuditSegmentHead expected)
    {
        if (head is null)
        {
            if (expected.Version != 0)
            {
                throw new AuditHeadConflictException(
                    $"The audit segment '{segment}' has no stored head but the append expected version {expected.Version}.");
            }

            return;
        }

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
    }

    /// <summary>
    ///     Applies the head an append computed onto the stored head entity.
    /// </summary>
    /// <param name="head">The stored head entity.</param>
    /// <param name="updated">The head after the append.</param>
    /// <remarks>
    ///     Every field is applied, including the identity: a placeholder head carries the identity staging
    ///     wrote, so the first append promotes the identity the segmenter described. Later appends keep the
    ///     stored identity because the engine passes it back unchanged.
    /// </remarks>
    private static void ApplyUpdatedHead(AuditSegmentHeadEntity head, AuditSegmentHead updated)
    {
        head.TenantId = updated.Info.TenantId;
        head.AttributesJson = AuditStoreJson.SerializeAttributes(updated.Info.Attributes);
        head.OpenedAt = updated.Info.OpenedAt;
        head.LastSequence = updated.LastSequence;
        head.LastHash = updated.LastHash;
        head.Count = updated.Count;
        head.LastCheckpointSequence = updated.LastCheckpointSequence;
        head.LastCheckpointAt = updated.LastCheckpointAt;
        head.Expired = updated.Expired;
        head.Version = updated.Version;
    }

    /// <summary>
    ///     Assigns a document to the segment an operation targets.
    /// </summary>
    /// <param name="segment">The segment the operation targets.</param>
    /// <param name="document">The document to assign.</param>
    /// <exception cref="InvalidOperationException">The document carries another segment.</exception>
    private static void AssignSegment(string segment, TDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Segment))
        {
            document.Segment = segment;
            return;
        }

        if (!string.Equals(document.Segment, segment, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The audit document carries segment '{document.Segment}' but the operation targets '{segment}'.");
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
    ///     Detaches a failed append head and its added documents, so the caller can retry on the same scope.
    /// </summary>
    /// <param name="dbContext">The database context the append ran on.</param>
    /// <param name="head">The head the append loaded or added, or <see langword="null" /> when the segment has none.</param>
    /// <param name="documents">The documents the append carried.</param>
    /// <remarks>
    ///     The head detaches even when the append failed before saving: a tracked head keeps the version the
    ///     failed pass read, and a retry would assert against that stale version instead of the winner.
    ///     Documents the scope already tracked keep their state, because the linker re-numbers those same
    ///     instances on its retry.
    /// </remarks>
    private static void DetachFailedAppend(DbContext dbContext, AuditSegmentHeadEntity? head, IReadOnlyList<TDocument> documents)
    {
        if (head is not null)
        {
            dbContext.Entry(head).State = EntityState.Detached;
        }

        foreach (var document in documents)
        {
            if (dbContext.Entry(document).State == EntityState.Added)
            {
                dbContext.Entry(document).State = EntityState.Detached;
            }
        }
    }

    /// <summary>
    ///     Fails staging when the audit mapping is incomplete.
    /// </summary>
    /// <param name="dbContext">The database context the staging runs on.</param>
    /// <exception cref="InvalidOperationException">The document type is not configured for audit storage.</exception>
    /// <remarks>
    ///     The probe reads the staged-at property the model configuration maps; a model that fails to build,
    ///     such as one whose head entities carry no key because the configuration never ran, fails here with
    ///     the same guidance and the build failure attached.
    /// </remarks>
    private static void ThrowIfDocumentNotConfigured(DbContext dbContext)
    {
        Exception? modelFailure = null;
        IProperty? stagedAt = null;

        try
        {
            stagedAt = dbContext.Model.FindEntityType(typeof(TDocument))?.FindProperty(AuditStoreJson.StagedAtPropertyName);
        }
        catch (InvalidOperationException exception)
        {
            modelFailure = exception;
        }

        if (stagedAt is null)
        {
            throw new InvalidOperationException(
                $"The audit document type '{typeof(TDocument).Name}' is not configured for audit storage. Call AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration from OnModelCreating.",
                modelFailure);
        }
    }

    /// <summary>
    ///     Adds a placeholder head for a segment that has none, without saving it.
    /// </summary>
    /// <param name="dbContext">The database context the staging runs on.</param>
    /// <param name="segment">The segment the staged document belongs to.</param>
    /// <param name="tenantId">The tenant of the staged document, or <see langword="null" /> when the document type carries none.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     The placeholder is added without saving, so it commits or rolls back with the business change and
    ///     the sweep discovers the segment only after the commit. When two scopes stage the first documents
    ///     of one segment at once, both add a placeholder and the second unit-of-work commit fails loudly
    ///     with a unique violation on the segment key instead of hiding the race.
    /// </remarks>
    private async ValueTask EnsurePlaceholderHeadAsync(
        DbContext dbContext,
        string segment,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Set<AuditSegmentHeadEntity>().Local.Any(head => string.Equals(head.Segment, segment, StringComparison.Ordinal)))
        {
            return;
        }

        var stored = await dbContext.Set<AuditSegmentHeadEntity>().AsNoTracking()
            .SingleOrDefaultAsync(head => head.Segment == segment, cancellationToken)
            .ConfigureAwait(false);

        if (stored is not null)
        {
            return;
        }

        dbContext.Set<AuditSegmentHeadEntity>().Add(new AuditSegmentHeadEntity
        {
            Segment = segment,
            TenantId = tenantId,
            OpenedAt = _timeProvider.GetUtcNow(),
        });
    }

    /// <summary>
    ///     Decides whether a document row already exists for a staged document primary key.
    /// </summary>
    /// <param name="dbContext">The database context the staging runs on.</param>
    /// <param name="document">The document about to be staged.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>
    ///     <see langword="true" /> when a row or a tracked instance already carries the key; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     The lookup runs through <c>FindAsync</c>, so a document the scope already tracks counts as stored
    ///     without a database round trip. A document type without a primary key, or a key with no value yet,
    ///     cannot deduplicate and always stages.
    /// </remarks>
    private static async ValueTask<bool> HasStoredDocumentAsync(DbContext dbContext, TDocument document, CancellationToken cancellationToken)
    {
        var entry = dbContext.Entry(document);
        var primaryKey = entry.Metadata.FindPrimaryKey();

        if (primaryKey is null)
        {
            return false;
        }

        var keyValues = new object?[primaryKey.Properties.Count];

        for (var index = 0; index < primaryKey.Properties.Count; index++)
        {
            keyValues[index] = entry.Property(primaryKey.Properties[index].Name).CurrentValue;
        }

        if (keyValues.Any(static value => value is null))
        {
            return false;
        }

        return await dbContext.Set<TDocument>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false) is not null;
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
    ///     Lists the stored head entities matching a filter, ordered by segment key.
    /// </summary>
    /// <param name="dbContext">The database context the listing runs on.</param>
    /// <param name="filter">The segments to list.</param>
    /// <param name="honorExpiry">
    ///     <see langword="true" /> to exclude expired segments unless the filter includes them;
    ///     <see langword="false" /> to match expired segments the same as open ones.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The matching head entities.</returns>
    /// <remarks>
    ///     The tenant, opened-before, and expiry predicates run in the database. The attribute predicates run
    ///     in memory against the stored attribute JSON, because heads are few: one row per segment.
    /// </remarks>
    private static async ValueTask<List<AuditSegmentHeadEntity>> ListMatchingHeadEntitiesAsync(
        DbContext dbContext,
        AuditSegmentFilter filter,
        bool honorExpiry,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<AuditSegmentHeadEntity>().AsNoTracking().AsQueryable();

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

        if (honorExpiry && !filter.IncludeExpired)
        {
            query = query.Where(head => !head.Expired);
        }

        var heads = await query
            .OrderBy(head => head.Segment)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (filter.Attributes is null || filter.Attributes.Count == 0)
        {
            return heads;
        }

        return heads
            .Where(head => MatchesAttributes(AuditStoreJson.DeserializeAttributes(head.AttributesJson), filter.Attributes))
            .ToList();
    }

    /// <summary>
    ///     Decides whether stored attributes carry every attribute a filter names.
    /// </summary>
    /// <param name="stored">The attributes stored on the head.</param>
    /// <param name="required">The required attributes.</param>
    /// <returns><see langword="true" /> when the stored attributes carry every required attribute value.</returns>
    private static bool MatchesAttributes(
        Dictionary<string, string> stored,
        IReadOnlyDictionary<string, string> required)
    {
        foreach (var pair in required)
        {
            if (!stored.TryGetValue(pair.Key, out var value) ||
                !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Reads one stored checkpoint entity by id, without tracking it.
    /// </summary>
    /// <param name="dbContext">The database context the read runs on.</param>
    /// <param name="checkpointId">The checkpoint identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The entity, or <see langword="null" /> when no checkpoint carries that id.</returns>
    private static async ValueTask<AuditCheckpointEntity?> ReadCheckpointEntityAsync(
        DbContext dbContext,
        Guid checkpointId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<AuditCheckpointEntity>().AsNoTracking()
            .SingleOrDefaultAsync(checkpoint => checkpoint.Id == checkpointId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads a stored head entity back as a segment head.
    /// </summary>
    /// <param name="entity">The stored head entity.</param>
    /// <returns>The segment head.</returns>
    private static AuditSegmentHead MapHead(AuditSegmentHeadEntity entity)
    {
        var info = new AuditSegmentInfo(
            entity.Segment,
            entity.TenantId,
            AuditStoreJson.DeserializeAttributes(entity.AttributesJson),
            entity.OpenedAt);

        return new AuditSegmentHead(
            info,
            entity.LastSequence,
            entity.LastHash,
            entity.Count,
            entity.LastCheckpointSequence,
            entity.LastCheckpointAt,
            entity.Expired,
            entity.Version);
    }

    /// <summary>
    ///     Reads a stored checkpoint entity back as a checkpoint.
    /// </summary>
    /// <param name="entity">The stored checkpoint entity.</param>
    /// <returns>The checkpoint.</returns>
    private static AuditCheckpoint MapCheckpoint(AuditCheckpointEntity entity)
    {
        return new AuditCheckpoint(
            entity.Id,
            entity.Segment,
            entity.Sequence,
            entity.Count,
            entity.Hash,
            entity.HashScheme,
            entity.Kind,
            entity.CreatedAt)
        {
            Signature = entity.SignatureScheme is null
                ? null
                : new AuditCheckpointSignature(entity.SignatureScheme, entity.SignatureValue ?? string.Empty),
            Label = entity.Label,
            PublishState = entity.PublishState,
            PublishAttempts = entity.PublishAttempts,
            LastPublishError = entity.LastPublishError,
            Signatures = AuditStoreJson.DeserializeSignatures(entity.SignaturesJson),
        };
    }

    /// <summary>
    ///     Creates a stored checkpoint entity from a checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint that was cut.</param>
    /// <returns>The stored checkpoint entity.</returns>
    private static AuditCheckpointEntity MapCheckpointEntity(AuditCheckpoint checkpoint)
    {
        return new AuditCheckpointEntity
        {
            Id = checkpoint.Id,
            Segment = checkpoint.Segment,
            Sequence = checkpoint.Sequence,
            Count = checkpoint.Count,
            Hash = checkpoint.Hash,
            HashScheme = checkpoint.HashScheme,
            Kind = checkpoint.Kind,
            CreatedAt = checkpoint.CreatedAt,
            SignatureScheme = checkpoint.Signature?.Scheme,
            SignatureValue = checkpoint.Signature?.Value,
            Label = checkpoint.Label,
            PublishState = checkpoint.PublishState,
            PublishAttempts = checkpoint.PublishAttempts,
            LastPublishError = checkpoint.LastPublishError,
            SignaturesJson = AuditStoreJson.SerializeSignatures(checkpoint.Signatures),
        };
    }

    /// <summary>
    ///     Decides whether a stored checkpoint entity carries the same evidence as a checkpoint.
    /// </summary>
    /// <param name="entity">The stored checkpoint entity.</param>
    /// <param name="checkpoint">The checkpoint stored again under the same identity.</param>
    /// <returns><see langword="true" /> when every evidence field matches.</returns>
    /// <remarks>
    ///     Only evidence participates: the publish state, attempt count, and last error are store-managed
    ///     tracking fields, so a cut that retries after a publish attempt still counts as the identical
    ///     checkpoint.
    /// </remarks>
    private static bool CheckpointsEqual(AuditCheckpointEntity entity, AuditCheckpoint checkpoint)
    {
        return string.Equals(entity.Segment, checkpoint.Segment, StringComparison.Ordinal) &&
            entity.Sequence == checkpoint.Sequence &&
            entity.Count == checkpoint.Count &&
            string.Equals(entity.Hash, checkpoint.Hash, StringComparison.Ordinal) &&
            string.Equals(entity.HashScheme, checkpoint.HashScheme, StringComparison.Ordinal) &&
            entity.Kind == checkpoint.Kind &&
            entity.CreatedAt == checkpoint.CreatedAt &&
            string.Equals(entity.Label, checkpoint.Label, StringComparison.Ordinal) &&
            SignatureEqual(entity, checkpoint) &&
            AuditStoreJson.DeserializeSignatures(entity.SignaturesJson).SequenceEqual(checkpoint.Signatures);
    }

    /// <summary>
    ///     Decides whether a stored checkpoint entity carries the single signature of a checkpoint.
    /// </summary>
    /// <param name="entity">The stored checkpoint entity.</param>
    /// <param name="checkpoint">The checkpoint stored again under the same identity.</param>
    /// <returns><see langword="true" /> when both carry no signature or the same scheme and value.</returns>
    private static bool SignatureEqual(AuditCheckpointEntity entity, AuditCheckpoint checkpoint)
    {
        if (checkpoint.Signature is null)
        {
            return entity.SignatureScheme is null;
        }

        return string.Equals(entity.SignatureScheme, checkpoint.Signature.Scheme, StringComparison.Ordinal) &&
            string.Equals(entity.SignatureValue, checkpoint.Signature.Value, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Detaches the tracked documents, heads, and checkpoints of erased segments, so a later save in the
    ///     same scope cannot write them back.
    /// </summary>
    /// <param name="dbContext">The database context the erasure ran on.</param>
    /// <param name="segments">The erased segments.</param>
    private static void PurgeTrackedScopeEntries(DbContext dbContext, List<string> segments)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<TDocument>())
        {
            if (entry.Entity.Segment is not null && segments.Contains(entry.Entity.Segment))
            {
                entry.State = EntityState.Detached;
            }
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<AuditSegmentHeadEntity>())
        {
            if (segments.Contains(entry.Entity.Segment))
            {
                entry.State = EntityState.Detached;
            }
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<AuditCheckpointEntity>())
        {
            if (segments.Contains(entry.Entity.Segment))
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
