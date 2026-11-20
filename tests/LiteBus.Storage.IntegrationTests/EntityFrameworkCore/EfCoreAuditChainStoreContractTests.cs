using System.Runtime.CompilerServices;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Runs the shared audit chain store contract tests against the Entity Framework Core audit chain store:
///     one database context per operation, staged documents committed on stage, and placeholder heads read
///     as no head.
/// </summary>
public sealed class EfCoreAuditChainStoreContractTests
    : AuditChainStoreContractTests<EfCoreAuditRecord>, IClassFixture<SqliteDatabaseFixture>
{
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditChainStoreContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreAuditChainStoreContractTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override IAuditChainStore<EfCoreAuditRecord> CreateStore()
    {
        ResetAuditTablesAsync().GetAwaiter().GetResult();

        return new PerOperationEfCoreAuditChainStore(CreateContext, new TickingClock(BaseTime));
    }

    /// <inheritdoc />
    protected override EfCoreAuditRecord CreateDocument(string marker)
    {
        return new EfCoreAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = marker,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = BaseTime,
            Category = "orders"
        };
    }

    /// <inheritdoc />
    protected override string GetMarker(EfCoreAuditRecord document)
    {
        return document.Action;
    }

    private async Task ResetAuditTablesAsync()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await context.Set<EfCoreAuditRecord>().ExecuteDeleteAsync().ConfigureAwait(false);
        await context.Set<AuditSegmentHeadEntity>().ExecuteDeleteAsync().ConfigureAwait(false);
        await context.Set<AuditCheckpointEntity>().ExecuteDeleteAsync().ConfigureAwait(false);
    }

    private EfCoreAuditTestDbContext CreateContext()
    {
        return new EfCoreAuditTestDbContext(
            new DbContextOptionsBuilder<EfCoreAuditTestDbContext>()
                .UseSqlite(_fixture.ConnectionString)
                .ReplaceService<IModelCacheKeyFactory, EfCoreAuditTestDbContext.ModelCacheKeyFactory>()
                .Options,
            new EntityFrameworkCoreAuditStoreOptions(),
            EfCoreStorageProvider.Sqlite);
    }

    private sealed class PerOperationEfCoreAuditChainStore : IAuditChainStore<EfCoreAuditRecord>
    {
        private readonly Func<EfCoreAuditTestDbContext> _createContext;
        private readonly TimeProvider _clock;

        public PerOperationEfCoreAuditChainStore(Func<EfCoreAuditTestDbContext> createContext, TimeProvider clock)
        {
            _createContext = createContext;
            _clock = clock;
        }

        public async ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
        {
            using var context = _createContext();
            var head = await CreateStore(context).ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

            // The suite stages without appending and then reads no head, while this store writes a
            // placeholder head on stage so its sweep discovers staged-only segments. The adapter
            // reports placeholders as no head so the suite proves the remaining port behavior.
            return IsPlaceholder(head) ? null : head;
        }

        public async ValueTask AppendAsync(
            string segment,
            AuditSegmentHead expected,
            IReadOnlyList<EfCoreAuditRecord> documents,
            AuditSegmentHead updated,
            CancellationToken cancellationToken)
        {
            using var context = _createContext();
            await CreateStore(context).AppendAsync(segment, expected, documents, updated, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask StageAsync(string segment, EfCoreAuditRecord document, CancellationToken cancellationToken)
        {
            // The store stages without saving, so staged documents commit with the business change. The
            // suite has no business change, so the adapter saves after every stage to make staged
            // documents visible to the unlinked listings.
            using var context = _createContext();
            await CreateStore(context).StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<EfCoreAuditRecord> ListUnlinkedAsync(
            string segment,
            TimeSpan? olderThan,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var context = _createContext();

            await foreach (var document in CreateStore(context).ListUnlinkedAsync(segment, olderThan, cancellationToken).ConfigureAwait(false))
            {
                yield return document;
            }
        }

        public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
            TimeSpan? olderThan,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var context = _createContext();

            await foreach (var segment in CreateStore(context).ListUnlinkedSegmentsAsync(olderThan, cancellationToken).ConfigureAwait(false))
            {
                yield return segment;
            }
        }

        public async IAsyncEnumerable<EfCoreAuditRecord> ReadSegmentAsync(
            string segment,
            long fromSequence,
            long? toSequence,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var context = _createContext();

            await foreach (var document in CreateStore(context).ReadSegmentAsync(segment, fromSequence, toSequence, cancellationToken).ConfigureAwait(false))
            {
                yield return document;
            }
        }

        public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
            AuditSegmentFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var context = _createContext();

            await foreach (var head in CreateStore(context).ListHeadsAsync(filter, cancellationToken).ConfigureAwait(false))
            {
                yield return head;
            }
        }

        public async ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            using var context = _createContext();
            await CreateStore(context).StoreCheckpointAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            using var context = _createContext();

            return await CreateStore(context).ReadCheckpointAsync(checkpointId, cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var context = _createContext();

            await foreach (var checkpoint in CreateStore(context).ListUnpublishedAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return checkpoint;
            }
        }

        public async ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
        {
            using var context = _createContext();
            await CreateStore(context).MarkPublishedAsync(checkpointId, publishError, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
        {
            using var context = _createContext();
            await CreateStore(context).EraseAsync(filter, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
        {
            using var context = _createContext();
            await CreateStore(context).ExpireAsync(segment, cancellationToken).ConfigureAwait(false);
        }

        private EfCoreAuditChainStore<EfCoreAuditRecord> CreateStore(EfCoreAuditTestDbContext context)
        {
            return new EfCoreAuditChainStore<EfCoreAuditRecord>(context, _clock);
        }

        private static bool IsPlaceholder(AuditSegmentHead? head)
        {
            return head is not null && head.Version == 0 && head.Count == 0 && head.LastSequence == 0 && head.LastHash is null;
        }
    }

    private sealed class TickingClock : TimeProvider
    {
        private readonly DateTimeOffset _start;
        private long _ticks;

        public TickingClock(DateTimeOffset start)
        {
            _start = start;
        }

        public override DateTimeOffset GetUtcNow()
        {
            // The store orders unlinked documents by staged-at, and stages landing in the same tick have
            // no defined order. Each clock read moves one second forward, giving every stage a distinct
            // stamp and a deterministic commit order.
            var tick = Interlocked.Increment(ref _ticks);

            return _start.AddSeconds(tick);
        }
    }
}
