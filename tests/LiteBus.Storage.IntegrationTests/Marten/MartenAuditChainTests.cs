using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Segments;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Auditing.Verification;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Integration tests for the Marten audit chain: checkpoints, expiry, custom documents, genesis records, scheme changes, and signatures.
/// </summary>
public sealed class MartenAuditChainTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChainTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenAuditChainTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task CutCheckpointAsync_stores_signs_and_publishes_a_checkpoint()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"cut-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var signer = new HashEchoCheckpointSigner();
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain
                .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
                .SignCheckpointsWith<HashEchoCheckpointSigner>()
                .PublishCheckpointsTo<RecordingCheckpointPublisher>(),
            services =>
            {
                services.AddSingleton(signer);
                services.AddSingleton(publisher);
            });
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        var checkpoint = await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        checkpoint.Segment.Should().Be(segment);
        checkpoint.Sequence.Should().Be(3);
        checkpoint.Count.Should().Be(3);
        checkpoint.Hash.Should().Be(records[2].Hash);
        checkpoint.HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
        checkpoint.Kind.Should().Be(AuditCheckpointKind.Regular);
        checkpoint.Id.Should().NotBeEmpty();
        checkpoint.Signature.Should().NotBeNull();
        checkpoint.Signature!.Scheme.Should().Be("test-echo");
        checkpoint.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        // The publisher receives the checkpoint read back from the store, whose timestamp loses
        // sub-microsecond precision, so the published checkpoint is compared field by field.
        var published = publisher.Published.Should().ContainSingle().Subject;
        published.Id.Should().Be(checkpoint.Id);
        published.Sequence.Should().Be(checkpoint.Sequence);
        published.Count.Should().Be(checkpoint.Count);
        published.Hash.Should().Be(checkpoint.Hash);
        published.HashScheme.Should().Be(checkpoint.HashScheme);
        published.Kind.Should().Be(checkpoint.Kind);
        published.Signature.Should().Be(checkpoint.Signature);
        published.CreatedAt.Should().BeCloseTo(checkpoint.CreatedAt, TimeSpan.FromMilliseconds(1));

        var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
        stored.Should().ContainSingle();
        stored[0].Id.Should().Be(checkpoint.Id);
        stored[0].Sequence.Should().Be(checkpoint.Sequence);
        stored[0].Count.Should().Be(checkpoint.Count);
        stored[0].Hash.Should().Be(checkpoint.Hash);
        stored[0].HashScheme.Should().Be(checkpoint.HashScheme);
        stored[0].Kind.Should().Be(checkpoint.Kind);
        stored[0].Signature.Should().Be(checkpoint.Signature);
        stored[0].CreatedAt.Should().BeCloseTo(checkpoint.CreatedAt, TimeSpan.FromMilliseconds(1));

        var head = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        head!.LastCheckpointSequence.Should().Be(3);
        head.LastCheckpointAt.Should().NotBeNull();
        head.LastCheckpointAt!.Value.Should().BeCloseTo(checkpoint.CreatedAt, TimeSpan.FromMilliseconds(1));

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.LatestCheckpoint.Should().NotBeNull();
        verification.LatestCheckpoint!.Id.Should().Be(checkpoint.Id);
        verification.LatestCheckpoint.Sequence.Should().Be(checkpoint.Sequence);
        verification.MatchesLatestCheckpoint.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeTrue();
    }

    [SkippableFact]
    public async Task CutCheckpointAsync_on_a_segment_without_records_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = $"missing-{Guid.NewGuid():N}/2026";
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredHarness = harness.ConfigureAwait(false);

        var cut = () => harness.Chain.CutCheckpointAsync(segment);
        var thrown = await cut.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*holds no records*");
    }

    [SkippableFact]
    public async Task ExpireSegmentAsync_publishes_a_final_checkpoint_and_removes_only_that_segment()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var expiringCategory = $"expiring-{Guid.NewGuid():N}";
        var expiringSegment = $"{expiringCategory}/2026";
        var remainingCategory = $"remaining-{Guid.NewGuid():N}";
        var remainingSegment = $"{remainingCategory}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain
                .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
                .PublishCheckpointsTo<RecordingCheckpointPublisher>(),
            services => services.AddSingleton(publisher));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", expiringCategory), expiringSegment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", expiringCategory), expiringSegment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("ledger.post-entry", remainingCategory), remainingSegment).ConfigureAwait(false);

        var final = await harness.Chain.ExpireSegmentAsync(expiringSegment).ConfigureAwait(false);

        final.Segment.Should().Be(expiringSegment);
        final.Sequence.Should().Be(2);
        final.Count.Should().Be(2);
        final.Kind.Should().Be(AuditCheckpointKind.Final);

        var published = publisher.Published.Should().ContainSingle().Subject;
        published.Id.Should().Be(final.Id);
        published.Kind.Should().Be(AuditCheckpointKind.Final);
        published.Sequence.Should().Be(final.Sequence);

        var expiredRecords = await FindAuditsAsync(documentStore, expiringSegment).ConfigureAwait(false);
        expiredRecords.Should().BeEmpty();

        var head = await LoadHeadAsync(documentStore, expiringSegment).ConfigureAwait(false);
        head!.Expired.Should().BeTrue();

        var stored = await FindCheckpointsAsync(documentStore, expiringSegment).ConfigureAwait(false);
        stored.Should().ContainSingle();
        stored[0].Kind.Should().Be(AuditCheckpointKind.Final);

        var remainingRecords = await FindAuditsAsync(documentStore, remainingSegment).ConfigureAwait(false);
        remainingRecords.Should().ContainSingle();

        await harness.CutAndPublishAsync(remainingSegment).ConfigureAwait(false);
        var remaining = await harness.Chain.VerifyAsync(remainingSegment).ConfigureAwait(false);
        remaining.IsIntact.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ExpireSegmentAsync_twice_throws_and_publishes_once()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"expire-twice-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain
                .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
                .PublishCheckpointsTo<RecordingCheckpointPublisher>(),
            services => services.AddSingleton(publisher));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);

        var first = await harness.Chain.ExpireSegmentAsync(segment).ConfigureAwait(false);
        first.Kind.Should().Be(AuditCheckpointKind.Final);

        var again = () => harness.Chain.ExpireSegmentAsync(segment);
        var thrown = await again.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*expired already*");

        publisher.Published.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task ExpireSegmentAsync_with_a_publish_failure_records_the_error_and_republishes_on_retry()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"expire-retry-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new FailOnceCheckpointPublisher();
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain
                .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
                .PublishCheckpointsTo<FailOnceCheckpointPublisher>(),
            services => services.AddSingleton(publisher));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);

        // A publisher failure no longer fails the expiry: the error is recorded on the checkpoint
        // and the expiry completes, so the retry publishes the same checkpoint afterwards.
        var final = await harness.Chain.ExpireSegmentAsync(segment).ConfigureAwait(false);

        final.Segment.Should().Be(segment);
        final.Sequence.Should().Be(2);
        final.Count.Should().Be(2);
        final.Kind.Should().Be(AuditCheckpointKind.Final);
        publisher.Published.Should().BeEmpty();

        var expiredRecords = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        expiredRecords.Should().BeEmpty();

        var expiredHead = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        expiredHead!.Expired.Should().BeTrue();

        var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
        var failed = stored.Should().ContainSingle().Subject;
        failed.Kind.Should().Be(AuditCheckpointKind.Final);
        failed.Sequence.Should().Be(2);
        failed.PublishState.Should().Be(CheckpointPublishState.Pending);
        failed.PublishAttempts.Should().Be(1);
        failed.LastPublishError.Should().Be("The checkpoint publisher failed.");

        var republished = await harness.Lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);
        republished.Should().Be(1);

        var published = publisher.Published.Should().ContainSingle().Subject;
        published.Id.Should().Be(final.Id);
        published.Kind.Should().Be(AuditCheckpointKind.Final);

        var afterRetry = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
        var anchored = afterRetry.Should().ContainSingle().Subject;
        anchored.PublishState.Should().Be(CheckpointPublishState.Published);
        anchored.PublishAttempts.Should().Be(2);
        anchored.LastPublishError.Should().BeNull();
    }

    [SkippableFact]
    public async Task Custom_document_chain_round_trip()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var workspace = $"workspace-{Guid.NewGuid():N}";
        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using var configuredBusiness = business.ConfigureAwait(false);
        var store = new MartenAuditChainStore<WorkspaceAuditDocument>(documentStore, business);
        var moduleBuilder = new AuditingModuleBuilder();
        moduleBuilder.UseHashChain(chain => chain
            .UseScheme("custom-v1")
            .VerifyAgainst<MartenAuditChainStore<WorkspaceAuditDocument>>());
        var chainOptions = moduleBuilder.Options.Chain!;
        var services = new ServiceCollection();
        services.AddSingleton(store);
        var provider = services.BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var schemes = new AuditHashSchemeRegistry(provider);
        schemes.Register(
            new AuditHashScheme<WorkspaceAuditDocument>(
                "custom-v1",
                new WorkspaceAuditCanonicalizer(),
                new PreviousHashLinkRule(),
                new Sha256AuditHasher()),
            "test");
        var segmenter = new MartenAuditFuncSegmenter<WorkspaceAuditDocument>(document => document.Workspace);
        var trail = new MartenAuditTrail<WorkspaceAuditDocument>(store, new WorkspaceAuditMapper(), segmenter);
        var linker = new AuditLinker<WorkspaceAuditDocument>(store, segmenter, schemes, chainOptions);
        var lifecycle = new CheckpointLifecycle<WorkspaceAuditDocument>(store, chainOptions, provider);
        var verifier = new AuditVerifier<WorkspaceAuditDocument>(store, schemes, chainOptions, provider);
        var segments = new AuditSegmentManager<WorkspaceAuditDocument>(store, lifecycle, chainOptions, TimeProvider.System);
        IAuditChain chain = new AuditChain(
            verifier,
            lifecycle,
            segments,
            new AuditCheckpointStore<WorkspaceAuditDocument>(store));

        await trail.WriteAsync(WorkspaceRecord("orders.place-order", workspace)).ConfigureAwait(false);
        await trail.WriteAsync(WorkspaceRecord("orders.reserve-stock", workspace)).ConfigureAwait(false);
        await linker.LinkPendingAsync(workspace, CancellationToken.None).ConfigureAwait(false);

        var documents = await FindCustomAuditsAsync(documentStore, workspace).ConfigureAwait(false);
        documents.Select(document => document.Sequence).Should().Equal(1L, 2L);
        documents[0].PreviousHash.Should().BeNull();
        documents[1].PreviousHash.Should().Be(documents[0].Hash);
        documents.Should().OnlyContain(document => document.HashScheme == "custom-v1");
        documents[0].Workspace.Should().Be(workspace);
        documents[0].RefusalCode.Should().Be("E_ORDER_FAILED");

        var head = await LoadHeadAsync(documentStore, workspace).ConfigureAwait(false);
        head!.LastSequence.Should().Be(2);
        head.HashScheme.Should().BeNull();

        await lifecycle.CutAsync(workspace, null, AuditCheckpointKind.Regular, CancellationToken.None).ConfigureAwait(false);
        await lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

        var verification = await chain.VerifyAsync(workspace).ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(2);
        verification.HeadSequence.Should().Be(2);
    }

    [SkippableFact]
    public async Task Genesis_record_opens_a_new_segment()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"genesis-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name),
            segmenter: new GenesisAuditSegmenter());
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L);
        records[0].Action.Should().Be("segment.opened");
        records[0].PreviousHash.Should().BeNull();
        records[1].Action.Should().Be("orders.place-order");
        records[1].PreviousHash.Should().Be(records[0].Hash);

        var head = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        head!.LastSequence.Should().Be(2);
        head.Count.Should().Be(2);

        await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(2);
    }

    [SkippableFact]
    public async Task Scheme_change_keeps_history_verifiable()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"rotation-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var before = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme(MartenAuditHashSchemes.Sha256V1.Name));
        await using var configuredBefore = before.ConfigureAwait(false);
        var after = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme("rotated-v1"));
        await using var configuredAfter = after.ConfigureAwait(false);
        after.Schemes.Register(
            new AuditHashScheme<MartenAuditRecord>(
                "rotated-v1",
                new MartenAuditRecordCanonicalizer(),
                new PreviousHashLinkRule(),
                new Sha256AuditHasher()),
            "test");

        await before.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await before.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await after.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L, 3L);
        records[0].HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
        records[2].HashScheme.Should().Be("rotated-v1");

        await after.CutAndPublishAsync(segment).ConfigureAwait(false);

        var rotatedVerification = await after.Chain.VerifyAsync(segment).ConfigureAwait(false);
        rotatedVerification.IsIntact.Should().BeTrue();
        rotatedVerification.RecordCount.Should().Be(3);

        // The legacy registry never registered the rotated scheme, which the shipped registry
        // reports as a configuration fault rather than a break.
        var legacy = () => before.Chain.VerifyAsync(segment);
        var thrown = await legacy.Should().ThrowAsync<LiteBusConfigurationException>().ConfigureAwait(false);
        thrown.WithMessage("*rotated-v1*");
    }

    [SkippableFact]
    public async Task Accepted_legacy_scheme_with_a_rotated_write_scheme_verifies()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"accepted-schemes-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var legacy = NamedScheme("legacy-v1");
        var rotated = NamedScheme("rotated-v1");
        var legacyHarness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme("legacy-v1"));
        await using var configuredLegacy = legacyHarness.ConfigureAwait(false);
        legacyHarness.Schemes.Register(legacy, "test");
        var rotatedHarness = MartenChainHarness.Create(
            documentStore,
            chain => chain.UseScheme("rotated-v1"));
        await using var configuredRotated = rotatedHarness.ConfigureAwait(false);
        rotatedHarness.Schemes.Register(rotated, "test");
        rotatedHarness.Schemes.Register(legacy, "test");

        await legacyHarness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await legacyHarness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await rotatedHarness.WriteAsync(FailedRecord("orders.charge-card", category), segment).ConfigureAwait(false);

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L, 3L);
        records[2].HashScheme.Should().Be("rotated-v1");

        await rotatedHarness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var verification = await rotatedHarness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(3);
    }

    [SkippableFact]
    public void Registering_two_schemes_sharing_a_name_throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditCanonicalizer<MartenAuditRecord>, MartenAuditRecordCanonicalizer>();
        using var provider = services.BuildServiceProvider();
        var schemes = new AuditHashSchemeRegistry(provider);
        var scheme = NamedScheme("test-duplicate-v1");

        schemes.Register(scheme, "first-source");
        schemes.Register(scheme, "first-source");

        var conflicting = () => schemes.Register(NamedScheme("test-duplicate-v1"), "second-source");

        conflicting.Should().Throw<LiteBusConfigurationException>().WithMessage("*first-source*second-source*");
    }

    [SkippableFact]
    public async Task Signed_checkpoints_detect_a_forged_signature()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"signed-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var signer = new HashEchoCheckpointSigner();
        var harness = MartenChainHarness.Create(
            documentStore,
            chain => chain
                .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
                .SignCheckpointsWith<HashEchoCheckpointSigner>(),
            services => services.AddSingleton(signer));
        await using var configuredHarness = harness.ConfigureAwait(false);

        await harness.WriteAsync(FailedRecord("orders.place-order", category), segment).ConfigureAwait(false);
        await harness.WriteAsync(FailedRecord("orders.reserve-stock", category), segment).ConfigureAwait(false);
        await harness.CutAndPublishAsync(segment).ConfigureAwait(false);

        var intact = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        intact.IsIntact.Should().BeTrue();
        intact.MatchesLatestCheckpoint.Should().BeTrue();
        intact.CheckpointSignatureValid.Should().BeTrue();

        var forge = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (forge.ConfigureAwait(false))
        {
            var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
            stored.Should().ContainSingle();
            stored[0].Signature = new AuditCheckpointSignature("test-echo", "forged");
            forge.Store(stored[0]);
            await forge.SaveChangesAsync().ConfigureAwait(false);
        }

        var forged = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        forged.IsIntact.Should().BeFalse();
        forged.CheckpointSignatureValid.Should().BeFalse();
        forged.Breaks.Should().Contain(breakdown => breakdown.Kind == AuditChainBreakKind.CheckpointSignatureInvalid);
    }

    [SkippableFact]
    public async Task Missing_covered_record_reports_a_count_mismatch()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"coverage-{Guid.NewGuid():N}";
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
            remove.Delete<MartenAuditRecord>(records[0].Id);
            await remove.SaveChangesAsync().ConfigureAwait(false);
        }

        var verification = await harness.Chain.VerifyAsync(segment).ConfigureAwait(false);
        verification.IsIntact.Should().BeFalse();
        verification.RecordCount.Should().Be(2);
        verification.Breaks.Should().Contain(breakdown =>
            breakdown.Kind == AuditChainBreakKind.Gap && breakdown.Sequence == 1);
        verification.Breaks.Should().Contain(breakdown =>
            breakdown.Kind == AuditChainBreakKind.CheckpointCountMismatch);
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
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Failed,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = category,
            FailureCode = "E_ORDER_FAILED"
        };
    }

    private static AuditRecord WorkspaceRecord(string action, string workspace)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Failed,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = "orders",
            FailureCode = "E_ORDER_FAILED",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workspace"] = workspace
            }
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

    private static async Task<IReadOnlyList<WorkspaceAuditDocument>> FindCustomAuditsAsync(
        IDocumentStore documentStore,
        string segment)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session
                .Query<WorkspaceAuditDocument>()
                .Where(document => document.Segment == segment)
                .OrderBy(document => document.Sequence)
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

    private static AuditHashScheme<MartenAuditRecord> NamedScheme(string name)
    {
        return new AuditHashScheme<MartenAuditRecord>(
            name,
            new MartenAuditRecordCanonicalizer(),
            new PreviousHashLinkRule(),
            new Sha256AuditHasher());
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
            IAuditChain chain,
            AuditHashSchemeRegistry schemes)
        {
            _business = business;
            _provider = provider;
            _linker = linker;
            Trail = trail;
            Lifecycle = lifecycle;
            Chain = chain;
            Schemes = schemes;
        }

        public MartenAuditTrail<MartenAuditRecord> Trail { get; }

        public CheckpointLifecycle<MartenAuditRecord> Lifecycle { get; }

        public IAuditChain Chain { get; }

        public AuditHashSchemeRegistry Schemes { get; }

        public static MartenChainHarness Create(
            DocumentStore documentStore,
            Action<AuditHashChainBuilder> configure,
            Action<ServiceCollection>? configureServices = null,
            IAuditSegmenter<MartenAuditRecord>? segmenter = null)
        {
            var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, business);
            segmenter ??= new MartenAuditRecordSegmenter();
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

            return new MartenChainHarness(business, provider, linker, trail, lifecycle, chain, schemes);
        }

        public async Task WriteAsync(AuditRecord record, string segment)
        {
            await Trail.WriteAsync(record).ConfigureAwait(false);
            await _business.SaveChangesAsync().ConfigureAwait(false);
            await _linker.LinkPendingAsync(segment, CancellationToken.None).ConfigureAwait(false);
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

    private sealed class FailOnceCheckpointPublisher : ICheckpointPublisher
    {
        private bool _failed;

        public List<AuditCheckpoint> Published { get; } = [];

        public ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("The checkpoint publisher failed.");
            }

            Published.Add(checkpoint);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HashEchoCheckpointSigner : ICheckpointSigner
    {
        public ValueTask<AuditCheckpointSignature> SignAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AuditCheckpointSignature("test-echo", checkpoint.Hash));
        }

        public ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                checkpoint.Signature is { Scheme: "test-echo" } signature && signature.Value == checkpoint.Hash);
        }
    }

    private sealed class WorkspaceAuditDocument : IAuditChainDocument
    {
        public Guid Id { get; set; }

        public string Action { get; set; } = string.Empty;

        public string Workspace { get; set; } = string.Empty;

        public string? RefusalCode { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }
    }

    private sealed class WorkspaceAuditMapper : IAuditRecordMapper<WorkspaceAuditDocument>
    {
        public WorkspaceAuditDocument Map(AuditRecord record)
        {
            record.Properties.TryGetValue("workspace", out var workspace);

            return new WorkspaceAuditDocument
            {
                Id = Guid.NewGuid(),
                Action = record.Action,
                Workspace = workspace ?? "default",
                RefusalCode = record.FailureCode,
                OccurredAt = record.OccurredAt
            };
        }
    }

    private sealed class WorkspaceAuditCanonicalizer : IAuditCanonicalizer<WorkspaceAuditDocument>
    {
        public byte[] Canonicalize(WorkspaceAuditDocument document)
        {
            var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["action"] = document.Action,
                ["id"] = document.Id.ToString(),
                ["occurred-at"] = document.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                ["previous-hash"] = document.PreviousHash,
                ["refusal-code"] = document.RefusalCode,
                ["segment"] = document.Segment,
                ["sequence"] = document.Sequence.ToString(CultureInfo.InvariantCulture),
                ["workspace"] = document.Workspace
            };

            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fields));
        }
    }

    private sealed class GenesisAuditSegmenter : IAuditSegmenter<MartenAuditRecord>
    {
        private readonly MartenAuditRecordSegmenter _segments = new();

        public string SegmentOf(MartenAuditRecord document)
        {
            return _segments.SegmentOf(document);
        }

        public MartenAuditRecord? CreateGenesis(string segment, MartenAuditRecord first)
        {
            return new MartenAuditRecord
            {
                Id = Guid.NewGuid(),
                Action = "segment.opened",
                Outcome = AuditOutcome.Succeeded,
                OccurredAt = first.OccurredAt,
                Category = first.Category
            };
        }
    }
}
