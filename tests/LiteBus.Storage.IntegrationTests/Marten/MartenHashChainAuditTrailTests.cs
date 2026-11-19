using System.Data;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Segments;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Auditing.Verification;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;
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
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(SuccessRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);

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
        head.HashScheme.Should().BeNull();
        head.Expired.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Write_scheme_defaults_to_sha256_v2_built_from_the_provider_canonicalizer()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"scheme-default-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var recording = new RecordingCanonicalizer();
        var harness = MartenChainHarness.Create(
            documentStore,
            _ => { },
            services => services.AddSingleton<IAuditCanonicalizer<MartenAuditRecord>>(recording));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);

        recording.Calls.Should().BeGreaterThan(0);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L);
        records.Should().OnlyContain(record => record.HashScheme == AuditHashChainOptions.DefaultSchemeName);

        await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(2);
    }

    [SkippableFact]
    public async Task Write_scheme_without_a_registered_canonicalizer_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"missing-canonicalizer-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using var configuredBusiness = business.ConfigureAwait(false);
        var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, business);
        var trail = new MartenAuditTrail<MartenAuditRecord>(
            store,
            new MartenAuditRecordMapper(),
            new MartenAuditRecordSegmenter());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var linker = new AuditLinker<MartenAuditRecord>(
            store,
            new MartenAuditRecordSegmenter(),
            new AuditHashSchemeRegistry(provider),
            new AuditHashChainOptions());

        await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
        await business.SaveChangesAsync().ConfigureAwait(false);

        var link = () => linker.LinkPendingAsync(segment, CancellationToken.None).AsTask();
        var thrown = await link.Should().ThrowAsync<LiteBusConfigurationException>().ConfigureAwait(false);
        thrown.WithMessage("*IAuditCanonicalizer*");
    }

    [SkippableFact]
    public async Task Checkpoint_is_published_every_interval()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"checkpoints-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain
                .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
                .CheckpointWhen(CheckpointPolicies.EveryRecords(2))
                .PublishCheckpointsTo<RecordingCheckpointPublisher>(),
            services => services.AddSingleton(publisher));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await harness.Lifecycle.CutDueAsync(CancellationToken.None).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.ship-order", category), segment).ConfigureAwait(false);
        await harness.Lifecycle.CutDueAsync(CancellationToken.None).ConfigureAwait(false);
        await harness.Lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);

        // The due-cut scan covers every head in the schema, so the published checkpoints of this
        // segment are selected before the count is asserted.
        var published = publisher.Published.Where(checkpoint => checkpoint.Segment == segment).ToList();
        published.Should().HaveCount(2);
        published[0].Segment.Should().Be(segment);
        published[0].Sequence.Should().Be(2);
        published[0].Count.Should().Be(2);
        published[0].Hash.Should().Be(records[1].Hash);
        published[0].HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
        published[0].Kind.Should().Be(AuditCheckpointKind.Regular);
        published[0].Id.Should().NotBeEmpty();
        published[1].Segment.Should().Be(segment);
        published[1].Sequence.Should().Be(4);
        published[1].Count.Should().Be(4);
        published[1].Hash.Should().Be(records[3].Hash);
        published[1].Id.Should().NotBe(published[0].Id);
        published[0].CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

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
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);

        var empty = await harness.Chain.VerifyAsync($"nothing-{Guid.NewGuid():N}").ConfigureAwait(false);
        empty.IsIntact.Should().BeTrue();
        empty.Breaks.Should().BeEmpty();
        empty.RecordCount.Should().Be(0);
        empty.LatestCheckpoint.Should().BeNull();
        empty.MatchesLatestCheckpoint.Should().BeNull();
        empty.CheckpointSignatureValid.Should().BeNull();

        await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.Segment.Should().Be(segment);
        verification.IsIntact.Should().BeTrue();
        verification.Breaks.Should().BeEmpty();
        verification.RecordCount.Should().Be(2);
        verification.HeadSequence.Should().Be(2);
        verification.HeadHash.Should().NotBeNull();
        verification.LatestCheckpoint.Should().NotBeNull();
        verification.MatchesLatestCheckpoint.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeNull();
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_a_tampered_record_reports_a_hash_mismatch()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"tampered-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);
        await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var tamper = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (tamper.ConfigureAwait(false))
        {
            var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
            records[1].Action = "orders.refund-order";
            tamper.Store(records[1]);
            await tamper.SaveChangesAsync().ConfigureAwait(false);
        }

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeFalse();
        verification.RecordCount.Should().Be(3);
        verification.Breaks.Should().ContainSingle();
        verification.Breaks[0].Sequence.Should().Be(2);
        verification.Breaks[0].Kind.Should().Be(AuditChainBreakKind.HashMismatch);
    }

    [SkippableFact]
    public async Task VerifyChainAsync_for_a_missing_record_reports_a_gap()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"gap-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);
        await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var remove = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (remove.ConfigureAwait(false))
        {
            var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
            remove.Delete<MartenAuditRecord>(records[1].Id);
            await remove.SaveChangesAsync().ConfigureAwait(false);
        }

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Should().Contain(breakdown => breakdown.Kind == AuditChainBreakKind.Gap && breakdown.Sequence == 2);
    }

    [SkippableFact]
    public async Task Default_segments_group_records_by_category_and_year()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"orders-{Guid.NewGuid():N}";
        var uncategorisedAction = $"ledger.post-entry-{Guid.NewGuid():N}";
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), $"{category}/2026").ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord(uncategorisedAction, null), "uncategorized/2026").ConfigureAwait(false);

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
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.Chain.ExpireSegmentAsync(segment).ConfigureAwait(false);

        // Staging accepts the record; linking it into the expired segment is what fails.
        await harness.Trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);

        var link = () => harness.LinkAsync(segment).AsTask();
        var thrown = await link.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*expired*");
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_auditing_hash_chain_tests";
        });
    }

    private static AuditRecord FailedRecord(string action, string? category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
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
            Id = Guid.NewGuid(),
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

    private sealed class MartenChainHarness : IAsyncDisposable
    {
        private readonly IDocumentSession _business;
        private readonly ServiceProvider _provider;
        private readonly IAuditLinker _linker;

        private MartenChainHarness(
            IDocumentSession business,
            ServiceProvider provider,
            IAuditLinker linker,
            MartenAuditTrail<MartenAuditRecord> trail,
            CheckpointLifecycle<MartenAuditRecord> lifecycle,
            IAuditChain chain)
        {
            _business = business;
            _provider = provider;
            _linker = linker;
            Trail = trail;
            Lifecycle = lifecycle;
            Chain = chain;
        }

        public MartenAuditTrail<MartenAuditRecord> Trail { get; }

        public CheckpointLifecycle<MartenAuditRecord> Lifecycle { get; }

        public IAuditChain Chain { get; }

        public static MartenChainHarness Create(
            DocumentStore documentStore,
            Action<AuditHashChainBuilder> configure,
            Action<ServiceCollection>? configureServices = null)
        {
            var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, business);
            var segmenter = new MartenAuditRecordSegmenter();
            var moduleBuilder = new AuditingModuleBuilder();
            moduleBuilder.UseHashChain(configure);
            moduleBuilder.UseHashChain(chain => chain.VerifyAgainst<MartenAuditChainStore<MartenAuditRecord>>());
            var chainOptions = moduleBuilder.Options.Chain!;
            var services = new ServiceCollection();
            services.AddSingleton(store);
            services.AddSingleton<IAuditCanonicalizer<MartenAuditRecord>, MartenAuditRecordCanonicalizer>();
            configureServices?.Invoke(services);
            var provider = services.BuildServiceProvider();
            var schemes = new AuditHashSchemeRegistry(provider);
            var linker = new AuditLinker<MartenAuditRecord>(store, segmenter, schemes, chainOptions);
            var lifecycle = new CheckpointLifecycle<MartenAuditRecord>(store, chainOptions, provider);
            var verifier = new AuditVerifier<MartenAuditRecord>(store, schemes, chainOptions, provider);
            var segments = new AuditSegmentManager<MartenAuditRecord>(store, lifecycle, chainOptions, TimeProvider.System);
            IAuditChain chain = new AuditChain(
                verifier,
                lifecycle,
                segments,
                new AuditCheckpointStore<MartenAuditRecord>(store));
            var trail = new MartenAuditTrail<MartenAuditRecord>(store, new MartenAuditRecordMapper(), segmenter);

            return new MartenChainHarness(business, provider, linker, trail, lifecycle, chain);
        }

        public async Task WriteAsync(AuditRecord record, string segment)
        {
            await Trail.WriteAsync(record).ConfigureAwait(false);
            await _business.SaveChangesAsync().ConfigureAwait(false);
            await _linker.LinkPendingAsync(segment, CancellationToken.None).ConfigureAwait(false);
        }

        public ValueTask LinkAsync(string segment)
        {
            return _linker.LinkPendingAsync(segment, CancellationToken.None);
        }

        public async Task<AuditCheckpoint> CutAndPublishAsync(string segment)
        {
            var checkpoint = await Lifecycle
                .CutAsync(segment, null, AuditCheckpointKind.Regular, CancellationToken.None)
                .ConfigureAwait(false);
            await Lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

            return checkpoint;
        }

        public async ValueTask DisposeAsync()
        {
            await _business.DisposeAsync().ConfigureAwait(false);
            await _provider.DisposeAsync().ConfigureAwait(false);
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

    private sealed class RecordingCanonicalizer : IAuditCanonicalizer<MartenAuditRecord>
    {
        private readonly MartenAuditRecordCanonicalizer _inner = new();

        public int Calls { get; private set; }

        public byte[] Canonicalize(MartenAuditRecord document)
        {
            Calls++;

            return _inner.Canonicalize(document);
        }
    }
}
