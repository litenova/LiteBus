using System.Data;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using LiteBus.Storage.Testing;
using Marten;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Runs the shared audit chain store contract tests against the Marten audit chain store, committing
///     each staged document so the suite reads it back.
/// </summary>
public sealed class MartenAuditChainStoreContractTests
    : AuditChainStoreContractTests<MartenAuditRecord>, IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private readonly List<TrackedMartenStore> _stores = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChainStoreContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenAuditChainStoreContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        for (var index = _stores.Count - 1; index >= 0; index--)
        {
            await _stores[index].DisposeAsync().ConfigureAwait(false);
        }

        _stores.Clear();
    }

    /// <inheritdoc />
    protected override IAuditChainStore<MartenAuditRecord> CreateStore()
    {
        var documentStore = DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_audit_contract_" + Guid.NewGuid().ToString("N");
        });
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, session, new TickingClock(BaseTime));
        _stores.Add(new TrackedMartenStore(documentStore, session));

        return new CommittingMartenAuditChainStore(store, session);
    }

    /// <inheritdoc />
    protected override MartenAuditRecord CreateDocument(string marker)
    {
        return new MartenAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = marker,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = BaseTime,
            Category = "orders"
        };
    }

    /// <inheritdoc />
    protected override string GetMarker(MartenAuditRecord document)
    {
        return document.Action;
    }

    private sealed class CommittingMartenAuditChainStore : IAuditChainStore<MartenAuditRecord>
    {
        private readonly IAuditChainStore<MartenAuditRecord> _inner;
        private readonly IDocumentSession _session;

        public CommittingMartenAuditChainStore(IAuditChainStore<MartenAuditRecord> inner, IDocumentSession session)
        {
            _inner = inner;
            _session = session;
        }

        public ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
        {
            return _inner.ReadHeadAsync(segment, cancellationToken);
        }

        public ValueTask AppendAsync(
            string segment,
            AuditSegmentHead expected,
            IReadOnlyList<MartenAuditRecord> documents,
            AuditSegmentHead updated,
            CancellationToken cancellationToken)
        {
            return _inner.AppendAsync(segment, expected, documents, updated, cancellationToken);
        }

        public async ValueTask StageAsync(string segment, MartenAuditRecord document, CancellationToken cancellationToken)
        {
            // The store stages onto the scoped session without saving, so staged documents commit with
            // the business change. The suite has no business change, so the adapter saves after every
            // stage to make staged documents visible to the unlinked listings.
            await _inner.StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public IAsyncEnumerable<MartenAuditRecord> ListUnlinkedAsync(
            string segment,
            TimeSpan? olderThan,
            CancellationToken cancellationToken)
        {
            return _inner.ListUnlinkedAsync(segment, olderThan, cancellationToken);
        }

        public IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(TimeSpan? olderThan, CancellationToken cancellationToken)
        {
            return _inner.ListUnlinkedSegmentsAsync(olderThan, cancellationToken);
        }

        public IAsyncEnumerable<MartenAuditRecord> ReadSegmentAsync(
            string segment,
            long fromSequence,
            long? toSequence,
            CancellationToken cancellationToken)
        {
            return _inner.ReadSegmentAsync(segment, fromSequence, toSequence, cancellationToken);
        }

        public IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
            AuditSegmentFilter filter,
            CancellationToken cancellationToken)
        {
            return _inner.ListHeadsAsync(filter, cancellationToken);
        }

        public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return _inner.StoreCheckpointAsync(checkpoint, cancellationToken);
        }

        public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            return _inner.ReadCheckpointAsync(checkpointId, cancellationToken);
        }

        public IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(CancellationToken cancellationToken)
        {
            return _inner.ListUnpublishedAsync(cancellationToken);
        }

        public ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
        {
            return _inner.MarkPublishedAsync(checkpointId, publishError, cancellationToken);
        }

        public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
        {
            return _inner.EraseAsync(filter, cancellationToken);
        }

        public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
        {
            return _inner.ExpireAsync(segment, cancellationToken);
        }
    }

    private sealed class TrackedMartenStore
    {
        private readonly IDocumentStore _documentStore;
        private readonly IDocumentSession _session;

        public TrackedMartenStore(IDocumentStore documentStore, IDocumentSession session)
        {
            _documentStore = documentStore;
            _session = session;
        }

        public async ValueTask DisposeAsync()
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            await _documentStore.DisposeAsync().ConfigureAwait(false);
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
            // Marten orders unlinked documents by staged-at with a random entry id as the tiebreak, so
            // stages landing in the same tick would list in random order. Each clock read moves one second
            // forward, giving every stage a distinct stamp and a deterministic commit order.
            var tick = Interlocked.Increment(ref _ticks);

            return _start.AddSeconds(tick);
        }
    }
}
