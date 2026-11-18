using System.Data;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Integration tests for the hash-chained Marten audit trail: sequencing, checkpoints, tamper detection, and verification.
/// </summary>
public sealed class MartenHashChainAuditTrailTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenHashChainAuditTrailTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenHashChainAuditTrailTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Chained_writes_assign_increasing_sequences_with_linked_hashes()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"chain-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(SuccessRecord("orders.reserve-stock", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L, 3L);
        records[0].PreviousHash.Should().BeNull();
        records[1].PreviousHash.Should().Be(records[0].Hash);
        records[2].PreviousHash.Should().Be(records[1].Hash);

        foreach (var record in records)
        {
            MartenAuditHashChain.ComputeHash(record).Should().Be(record.Hash);
        }

        var head = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.LastSequence.Should().Be(3);
        head.LastHash.Should().Be(records[2].Hash);
    }

    [SkippableFact]
    public async Task Checkpoint_is_published_every_interval()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"checkpoints-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var options = new MartenAuditTrailOptions
        {
            CheckpointInterval = 2,
            CheckpointPublisher = publisher
        };
        options.UseHashChain();

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.ship-order", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);

        publisher.Published.Should().HaveCount(2);
        publisher.Published[0].Segment.Should().Be(segment);
        publisher.Published[0].Sequence.Should().Be(2);
        publisher.Published[0].Hash.Should().Be(records[1].Hash);
        publisher.Published[1].Segment.Should().Be(segment);
        publisher.Published[1].Sequence.Should().Be(4);
        publisher.Published[1].Hash.Should().Be(records[3].Hash);
        publisher.Published[0].CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_an_intact_chain_returns_null()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"intact-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);

            var broken = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            broken.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_a_tampered_record_returns_its_sequence()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"tampered-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var tamper = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (tamper.ConfigureAwait(false))
        {
            var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
            records[1].Action = "orders.refund-order";
            tamper.Store(records[1]);
            await tamper.SaveChangesAsync().ConfigureAwait(false);
        }

        var verify = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (verify.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(verify, documentStore, new MartenAuditTrailOptions().UseHashChain());
            var broken = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            broken.Should().Be(2);
        }
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_a_missing_record_returns_the_missing_sequence()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"gap-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var remove = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (remove.ConfigureAwait(false))
        {
            var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
            remove.Delete<MartenAuditRecord>(records[1].Id);
            await remove.SaveChangesAsync().ConfigureAwait(false);
        }

        var verify = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (verify.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(verify, documentStore, new MartenAuditTrailOptions().UseHashChain());
            var broken = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            broken.Should().Be(2);
        }
    }

    [SkippableFact]
    public async Task Chained_success_record_persists_without_the_scoped_commit()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"chained-success-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";

        // The scoped session is disposed without saving: a chained record commits on its own session instead.
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());
            await trail.WriteAsync(SuccessRecord("orders.place-order", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        var record = records.Should().ContainSingle().Subject;
        record.Sequence.Should().Be(1);
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
    }

    [SkippableFact]
    public async Task Default_segments_group_records_by_category_and_year()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"orders-{Guid.NewGuid():N}";
        var uncategorisedAction = $"ledger.post-entry-{Guid.NewGuid():N}";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord(uncategorisedAction, null)).ConfigureAwait(false);
        }

        var categorised = await FindAuditsAsync(documentStore, $"{category}/2026").ConfigureAwait(false);
        var record = categorised.Should().ContainSingle().Subject;
        record.Sequence.Should().Be(1);

        var uncategorised = await FindAuditsAsync(documentStore, "uncategorized/2026").ConfigureAwait(false);
        uncategorised.Should().Contain(single => single.Action == uncategorisedAction);
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_audit_chain_tests";
        });
    }

    private static AuditRecord FailedRecord(string action, string? category)
    {
        return new AuditRecord
        {
            Action = action,
            Outcome = AuditOutcome.Failed,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = category,
            FailureCode = "E_ORDER_FAILED"
        };
    }

    private static AuditRecord SuccessRecord(string action, string? category)
    {
        return new AuditRecord
        {
            Action = action,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = category
        };
    }

    private static async Task<IReadOnlyList<MartenAuditRecord>> FindAuditsAsync(
        IDocumentStore documentStore,
        string segment)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session
                .Query<MartenAuditRecord>()
                .Where(record => record.Segment == segment)
                .OrderBy(record => record.Sequence)
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task<MartenAuditSegmentHead?> LoadHeadAsync(IDocumentStore documentStore, string segment)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session.LoadAsync<MartenAuditSegmentHead>(segment).ConfigureAwait(false);
        }
    }

    private sealed class RecordingCheckpointPublisher : ICheckpointPublisher
    {
        public List<AuditCheckpoint> Published { get; } = [];

        public ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            Published.Add(checkpoint);
            return ValueTask.CompletedTask;
        }
    }
}
