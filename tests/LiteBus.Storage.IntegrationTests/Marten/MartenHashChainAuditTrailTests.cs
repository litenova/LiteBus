using System.Data;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task Chained_writes_assign_increasing_sequences_with_linked_hashes_and_scheme()
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
        records.Should().OnlyContain(record => record.HashScheme == MartenAuditHashSchemes.Sha256V1.Name);

        var head = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.LastSequence.Should().Be(3);
        head.LastHash.Should().Be(records[2].Hash);
        head.Count.Should().Be(3);
        head.HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
        head.Expired.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Write_scheme_factory_builds_the_scheme_from_the_trail_service_provider()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"scheme-factory-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var services = new ServiceCollection();
        services.AddSingleton(new SchemeName("sp-scheme-v1"));
        var serviceProvider = services.BuildServiceProvider();
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);
        IServiceProvider? factoryProvider = null;
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.UseScheme<MartenAuditRecord>(provider =>
        {
            factoryProvider = provider;

            return TestScheme(provider.GetService<SchemeName>()!.Value);
        }));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options, serviceProvider: serviceProvider);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
        }

        factoryProvider.Should().BeSameAs(serviceProvider);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L);
        records.Should().OnlyContain(record => record.HashScheme == "sp-scheme-v1");

        var verification = await new MartenAuditChain(documentStore, options, serviceProvider: serviceProvider)
            .VerifyAsync(segment)
            .ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(2);
    }

    [SkippableFact]
    public async Task Write_scheme_factory_without_a_service_provider_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.UseScheme<MartenAuditRecord>(_ => TestScheme("test-factory-v1")));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            var write = () => trail.WriteAsync(FailedRecord("orders.place-order", "orders"));
            var thrown = await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*service provider*");
        }
    }

    [SkippableFact]
    public async Task Checkpoint_is_published_every_interval()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"checkpoints-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain
            .CheckpointWhen(CheckpointPolicies.EveryRecords(2))
            .PublishCheckpointsTo(publisher));

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
        publisher.Published[0].Count.Should().Be(2);
        publisher.Published[0].Hash.Should().Be(records[1].Hash);
        publisher.Published[0].HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
        publisher.Published[0].Kind.Should().Be(AuditCheckpointKind.Regular);
        publisher.Published[0].Id.Should().NotBeEmpty();
        publisher.Published[1].Segment.Should().Be(segment);
        publisher.Published[1].Sequence.Should().Be(4);
        publisher.Published[1].Count.Should().Be(4);
        publisher.Published[1].Hash.Should().Be(records[3].Hash);
        publisher.Published[1].Id.Should().NotBe(publisher.Published[0].Id);
        publisher.Published[0].CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
        stored.Select(checkpoint => checkpoint.Sequence).Should().Equal(2L, 4L);

        var head = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        head!.LastCheckpointSequence.Should().Be(4);
        head.LastCheckpointAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_an_intact_chain_reports_intact()
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

            var verification = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            verification.Segment.Should().Be(segment);
            verification.IsIntact.Should().BeTrue();
            verification.Breaks.Should().BeEmpty();
            verification.RecordCount.Should().Be(2);
            verification.HeadSequence.Should().Be(2);
            verification.HeadHash.Should().NotBeNull();
            verification.LatestCheckpoint.Should().BeNull();
            verification.MatchesLatestCheckpoint.Should().BeNull();
            verification.CheckpointSignatureValid.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_a_tampered_record_reports_a_hash_mismatch()
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
            var verification = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            verification.IsIntact.Should().BeFalse();
            verification.RecordCount.Should().Be(3);
            verification.Breaks.Should().ContainSingle();
            verification.Breaks[0].Sequence.Should().Be(2);
            verification.Breaks[0].Kind.Should().Be(AuditChainBreakKind.HashMismatch);
        }
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_a_missing_record_reports_a_gap()
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
            var verification = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            verification.IsIntact.Should().BeFalse();
            verification.Breaks.Should().Contain(breakdown => breakdown.Kind == AuditChainBreakKind.Gap && breakdown.Sequence == 2);
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
        record.HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
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

    [SkippableFact]
    public async Task Appending_to_an_expired_segment_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"expired-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var options = new MartenAuditTrailOptions().UseHashChain();

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await new MartenAuditChain(documentStore, options).ExpireSegmentAsync(segment).ConfigureAwait(false);

            var append = () => trail.WriteAsync(FailedRecord("orders.reserve-stock", category));
            var thrown = await append.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*expired*");
        }
    }

    [SkippableFact]
    public async Task Records_written_before_schemes_verify_unchanged()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"legacy-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, new MartenAuditTrailOptions().UseHashChain());

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
        }

        // Records written before schemes carry no scheme name; clearing it simulates that storage shape.
        var backdate = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (backdate.ConfigureAwait(false))
        {
            var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);

            foreach (var record in records)
            {
                record.HashScheme = null;
                backdate.Store(record);
            }

            await backdate.SaveChangesAsync().ConfigureAwait(false);
        }

        var verify = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (verify.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(verify, documentStore, new MartenAuditTrailOptions().UseHashChain());
            var verification = await trail.VerifyChainAsync(segment).ConfigureAwait(false);
            verification.IsIntact.Should().BeTrue();
            verification.Breaks.Should().BeEmpty();
            verification.RecordCount.Should().Be(2);
        }
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

    private static async Task<IReadOnlyList<MartenAuditCheckpoint>> FindCheckpointsAsync(
        IDocumentStore documentStore,
        string segment)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session
                .Query<MartenAuditCheckpoint>()
                .Where(checkpoint => checkpoint.Segment == segment)
                .OrderBy(checkpoint => checkpoint.Sequence)
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

    private static AuditHashScheme<MartenAuditRecord> TestScheme(string name)
    {
        return new AuditHashScheme<MartenAuditRecord>(
            name,
            new MartenAuditRecordCanonicalizer(),
            new PreviousHashLinkRule(),
            new Sha256AuditHasher());
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

    private sealed record SchemeName(string Value);
}
