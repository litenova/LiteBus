using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;
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
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain
            .SignCheckpointsWith(new HashEchoCheckpointSigner())
            .PublishCheckpointsTo(publisher));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        var checkpoints = new MartenAuditChain(documentStore, options);
        var checkpoint = await checkpoints.CutCheckpointAsync(segment).ConfigureAwait(false);

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

        publisher.Published.Should().ContainSingle();
        publisher.Published[0].Should().Be(checkpoint);

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

        var verification = await checkpoints.VerifyAsync(segment).ConfigureAwait(false);
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
        var options = new MartenAuditTrailOptions().UseHashChain();
        var checkpoints = new MartenAuditChain(documentStore, options);

        var cut = () => checkpoints.CutCheckpointAsync(segment);
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
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.PublishCheckpointsTo(publisher));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", expiringCategory)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", expiringCategory)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("ledger.post-entry", remainingCategory)).ConfigureAwait(false);
        }

        var checkpoints = new MartenAuditChain(documentStore, options);
        var final = await checkpoints.ExpireSegmentAsync(expiringSegment).ConfigureAwait(false);

        final.Segment.Should().Be(expiringSegment);
        final.Sequence.Should().Be(2);
        final.Count.Should().Be(2);
        final.Kind.Should().Be(AuditCheckpointKind.Final);
        publisher.Published.Should().ContainSingle();
        publisher.Published[0].Should().Be(final);

        var expiredRecords = await FindAuditsAsync(documentStore, expiringSegment).ConfigureAwait(false);
        expiredRecords.Should().BeEmpty();

        var head = await LoadHeadAsync(documentStore, expiringSegment).ConfigureAwait(false);
        head!.Expired.Should().BeTrue();

        var stored = await FindCheckpointsAsync(documentStore, expiringSegment).ConfigureAwait(false);
        stored.Should().ContainSingle();
        stored[0].Kind.Should().Be(AuditCheckpointKind.Final);

        var remainingRecords = await FindAuditsAsync(documentStore, remainingSegment).ConfigureAwait(false);
        remainingRecords.Should().ContainSingle();

        var remaining = await checkpoints.VerifyAsync(remainingSegment).ConfigureAwait(false);
        remaining.IsIntact.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ExpireSegmentAsync_is_idempotent()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"expire-twice-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new RecordingCheckpointPublisher();
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.PublishCheckpointsTo(publisher));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);
            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
        }

        var checkpoints = new MartenAuditChain(documentStore, options);
        var first = await checkpoints.ExpireSegmentAsync(segment).ConfigureAwait(false);
        var second = await checkpoints.ExpireSegmentAsync(segment).ConfigureAwait(false);

        second.Id.Should().Be(first.Id);
        second.Kind.Should().Be(AuditCheckpointKind.Final);
        second.Sequence.Should().Be(first.Sequence);
        publisher.Published.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task ExpireSegmentAsync_after_a_publish_failure_republishes_the_same_checkpoint_and_deletes()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"expire-retry-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publisher = new FailOnceCheckpointPublisher();
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.PublishCheckpointsTo(publisher));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
        }

        var checkpoints = new MartenAuditChain(documentStore, options);
        var expire = () => checkpoints.ExpireSegmentAsync(segment);
        var thrown = await expire.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*publisher*");

        publisher.Published.Should().BeEmpty();

        var retained = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        retained.Select(record => record.Sequence).Should().Equal(1L, 2L);

        var openHead = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        openHead!.Expired.Should().BeFalse();

        var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
        var final = stored.Should().ContainSingle().Subject;
        final.Kind.Should().Be(AuditCheckpointKind.Final);
        final.Sequence.Should().Be(2);

        var retried = await checkpoints.ExpireSegmentAsync(segment).ConfigureAwait(false);

        retried.Id.Should().Be(final.Id);
        retried.Kind.Should().Be(AuditCheckpointKind.Final);
        publisher.Published.Should().ContainSingle();
        publisher.Published[0].Id.Should().Be(final.Id);

        var expiredRecords = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        expiredRecords.Should().BeEmpty();

        var expiredHead = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        expiredHead!.Expired.Should().BeTrue();

        var again = await checkpoints.ExpireSegmentAsync(segment).ConfigureAwait(false);
        again.Id.Should().Be(final.Id);
        publisher.Published.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Custom_document_chain_round_trip()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var workspace = $"workspace-{Guid.NewGuid():N}";
        var scheme = new AuditHashScheme<WorkspaceAuditDocument>(
            "custom-v1",
            new WorkspaceAuditCanonicalizer(),
            new PreviousHashLinkRule(),
            new Sha256AuditHasher());
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain
            .StoreAs<WorkspaceAuditDocument>(new WorkspaceAuditMapper())
            .SegmentBy<WorkspaceAuditDocument>(document => document.Workspace)
            .UseScheme(scheme));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(WorkspaceRecord("orders.place-order", workspace)).ConfigureAwait(false);
            await trail.WriteAsync(WorkspaceRecord("orders.reserve-stock", workspace)).ConfigureAwait(false);
        }

        var documents = await FindCustomAuditsAsync(documentStore, workspace).ConfigureAwait(false);
        documents.Select(document => document.Sequence).Should().Equal(1L, 2L);
        documents[0].PreviousHash.Should().BeNull();
        documents[1].PreviousHash.Should().Be(documents[0].Hash);
        documents.Should().OnlyContain(document => document.HashScheme == "custom-v1");
        documents[0].Workspace.Should().Be(workspace);
        documents[0].RefusalCode.Should().Be("E_ORDER_FAILED");

        var head = await LoadHeadAsync(documentStore, workspace).ConfigureAwait(false);
        head!.LastSequence.Should().Be(2);
        head.HashScheme.Should().Be("custom-v1");

        var verification = await new MartenAuditChain(documentStore, options).VerifyAsync(workspace).ConfigureAwait(false);
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
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.SegmentBy(new GenesisAuditSegmenter()));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);
            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L);
        records[0].Action.Should().Be("segment.opened");
        records[0].PreviousHash.Should().BeNull();
        records[1].Action.Should().Be("orders.place-order");
        records[1].PreviousHash.Should().Be(records[0].Hash);

        var head = await LoadHeadAsync(documentStore, segment).ConfigureAwait(false);
        head!.LastSequence.Should().Be(2);
        head.Count.Should().Be(2);

        var verification = await new MartenAuditChain(documentStore, options).VerifyAsync(segment).ConfigureAwait(false);
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
        var rotated = new AuditHashScheme<MartenAuditRecord>(
            "sha256-v2",
            new MartenAuditRecordCanonicalizer(),
            new PreviousHashLinkRule(),
            new Sha256AuditHasher());
        var before = new MartenAuditTrailOptions().UseHashChain();
        var after = new MartenAuditTrailOptions();
        after.UseHashChain(chain => chain
            .UseScheme(rotated)
            .AcceptScheme(MartenAuditHashSchemes.Sha256V1));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var oldTrail = new MartenAuditTrail(session, documentStore, before);

            await oldTrail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await oldTrail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);

            var newTrail = new MartenAuditTrail(session, documentStore, after);
            await newTrail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L, 3L);
        records[0].HashScheme.Should().Be(MartenAuditHashSchemes.Sha256V1.Name);
        records[2].HashScheme.Should().Be("sha256-v2");

        var rotatedVerification = await new MartenAuditChain(documentStore, after).VerifyAsync(segment).ConfigureAwait(false);
        rotatedVerification.IsIntact.Should().BeTrue();
        rotatedVerification.RecordCount.Should().Be(3);

        var legacyVerification = await new MartenAuditChain(documentStore, before).VerifyAsync(segment).ConfigureAwait(false);
        legacyVerification.IsIntact.Should().BeFalse();
        legacyVerification.Breaks.Should().Contain(breakdown =>
            breakdown.Kind == AuditChainBreakKind.UnknownHashScheme && breakdown.Sequence == 3);
    }

    [SkippableFact]
    public async Task Accepted_scheme_factory_resolves_from_the_chain_service_provider()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"accepted-factory-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var legacy = NamedScheme("legacy-v1");
        var rotated = NamedScheme("rotated-v1");
        var before = new MartenAuditTrailOptions();
        before.UseHashChain(chain => chain.UseScheme(legacy));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, before);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
        }

        var services = new ServiceCollection();
        services.AddSingleton(legacy);
        var serviceProvider = services.BuildServiceProvider();
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);
        var after = new MartenAuditTrailOptions();
        after.UseHashChain(chain => chain
            .UseScheme(rotated)
            .AcceptScheme<MartenAuditRecord>(provider => provider.GetService<AuditHashScheme<MartenAuditRecord>>()!));

        var write = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (write.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(write, documentStore, after, serviceProvider: serviceProvider);

            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
        records.Select(record => record.Sequence).Should().Equal(1L, 2L, 3L);
        records[2].HashScheme.Should().Be("rotated-v1");

        var verification = await new MartenAuditChain(documentStore, after, serviceProvider: serviceProvider)
            .VerifyAsync(segment)
            .ConfigureAwait(false);
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(3);
    }

    [SkippableFact]
    public async Task VerifyAsync_with_two_factory_schemes_sharing_a_name_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = $"duplicate-schemes-{Guid.NewGuid():N}/2026";
        var services = new ServiceCollection();
        services.AddSingleton(new SchemeName("test-duplicate-v1"));
        var serviceProvider = services.BuildServiceProvider();
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain
            .UseScheme<MartenAuditRecord>(provider => NamedScheme(provider.GetService<SchemeName>()!.Value))
            .AcceptScheme<MartenAuditRecord>(provider => NamedScheme(provider.GetService<SchemeName>()!.Value)));

        var verify = () => new MartenAuditChain(documentStore, options, serviceProvider: serviceProvider).VerifyAsync(segment);
        var thrown = await verify.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*unique*");
    }

    [SkippableFact]
    public async Task Signed_checkpoints_detect_a_forged_signature()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"signed-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain
            .CheckpointWhen(CheckpointPolicies.EveryRecords(2))
            .SignCheckpointsWith(new HashEchoCheckpointSigner()));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
        }

        var checkpoints = new MartenAuditChain(documentStore, options);
        var intact = await checkpoints.VerifyAsync(segment).ConfigureAwait(false);
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

        var forged = await checkpoints.VerifyAsync(segment).ConfigureAwait(false);
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
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.CheckpointWhen(CheckpointPolicies.EveryRecords(2)));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);

            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.reserve-stock", category)).ConfigureAwait(false);
            await trail.WriteAsync(FailedRecord("orders.charge-card", category)).ConfigureAwait(false);
        }

        var remove = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (remove.ConfigureAwait(false))
        {
            var records = await FindAuditsAsync(documentStore, segment).ConfigureAwait(false);
            remove.Delete<MartenAuditRecord>(records[0].Id);
            await remove.SaveChangesAsync().ConfigureAwait(false);
        }

        var verification = await new MartenAuditChain(documentStore, options).VerifyAsync(segment).ConfigureAwait(false);
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

    private sealed record SchemeName(string Value);

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
