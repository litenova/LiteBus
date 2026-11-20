using System.Data;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Integration tests for the Marten audit chain store: heads with optimistic concurrency, staged documents in
///     commit order, sequence paging, head filtering, checkpoint publish state, and erasure.
/// </summary>
public sealed class MartenAuditChainStoreTests : IClassFixture<PostgreSqlFixture>
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChainStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenAuditChainStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task ReadHeadAsync_for_an_unknown_segment_returns_null()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            var head = await store
                .ReadHeadAsync($"unknown-{Guid.NewGuid():N}", CancellationToken.None)
                .ConfigureAwait(false);

            head.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task AppendAsync_creates_the_head_and_stores_the_documents()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("append");
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var expected = InitialHead(segment, "tenant-a");
            MartenAuditRecord[] documents =
            [
                LinkedDocument(segment, 1, null, "hash-1"),
                LinkedDocument(segment, 2, "hash-1", "hash-2")
            ];
            var updated = expected with
            {
                LastSequence = 2,
                LastHash = "hash-2",
                Count = 2,
                Version = 1
            };

            await store
                .AppendAsync(segment, expected, documents, updated, CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().NotBeNull();
            head!.Info.Key.Should().Be(segment);
            head.Info.TenantId.Should().Be("tenant-a");
            head.Info.Attributes.Should().BeEquivalentTo(updated.Info.Attributes);
            head.Info.OpenedAt.Should().Be(updated.Info.OpenedAt);
            head.LastSequence.Should().Be(2);
            head.LastHash.Should().Be("hash-2");
            head.Count.Should().Be(2);
            head.Expired.Should().BeFalse();
            head.Version.Should().Be(1);

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Select(document => document.Sequence).Should().Equal(1L, 2L);
            stored[1].PreviousHash.Should().Be("hash-1");
        }
    }

    [SkippableFact]
    public async Task AppendAsync_with_a_stale_version_throws_a_head_conflict()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("stale");
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var expected = InitialHead(segment, "tenant-a");
            var updated = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], updated, CancellationToken.None)
                .ConfigureAwait(false);

            var stale = () => store.AppendAsync(
                segment,
                expected,
                [LinkedDocument(segment, 1, null, "hash-other")],
                updated,
                CancellationToken.None).AsTask();

            var thrown = await stale.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false);
            thrown.WithMessage($"*{segment}*");
        }
    }

    [SkippableFact]
    public async Task Concurrent_appends_assign_unique_sequences_without_gaps()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("concurrent");
        var info = SegmentInfo(segment, "tenant-a");

        var appends = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
            await using (session.ConfigureAwait(false))
            {
                var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
                var document = LinkedDocument(segment, 0, null, $"hash-{index}");

                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
                    var expected = head ?? new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
                    var sequence = expected.LastSequence + 1;
                    document.Sequence = sequence;
                    document.PreviousHash = expected.LastHash;
                    var updated = expected with
                    {
                        LastSequence = sequence,
                        LastHash = document.Hash,
                        Count = expected.Count + 1,
                        Version = expected.Version + 1
                    };

                    try
                    {
                        await store
                            .AppendAsync(segment, expected, [document], updated, CancellationToken.None)
                            .ConfigureAwait(false);

                        return;
                    }
                    catch (AuditHeadConflictException)
                    {
                        // Another writer committed first; the next attempt reads the winner.
                    }
                }

                throw new InvalidOperationException($"The append for document {index} never committed.");
            }
        })).ToList();

        await Task.WhenAll(appends).ConfigureAwait(false);

        var verify = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (verify.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);

            stored.Select(document => document.Sequence).Should().Equal(1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.LastSequence.Should().Be(8);
            head.Count.Should().Be(8);
            head.Version.Should().Be(8);
            head.LastHash.Should().Be(stored[7].Hash);
        }
    }

    [SkippableFact]
    public async Task AppendAsync_to_an_expired_head_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("expired");
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var expected = InitialHead(segment, "tenant-a");
            var linked = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], linked, CancellationToken.None)
                .ConfigureAwait(false);

            var expired = linked with { Expired = true, Version = 2 };

            await store
                .AppendAsync(segment, linked, [], expired, CancellationToken.None)
                .ConfigureAwait(false);

            var append = () => store.AppendAsync(
                segment,
                expired,
                [LinkedDocument(segment, 2, "hash-1", "hash-2")],
                expired with { LastSequence = 2, LastHash = "hash-2", Count = 2, Version = 3 },
                CancellationToken.None).AsTask();

            var thrown = await append.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*expired*");
        }
    }

    [SkippableFact]
    public async Task Staged_documents_list_in_commit_order()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("staged");
        var clock = new ManualClock(OpenedAt);
        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, clock);

            await store.StageAsync(business, segment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(1));
            await store.StageAsync(business, segment, UnlinkedDocument("orders.reserve-stock"), CancellationToken.None).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(1));
            await store.StageAsync(business, segment, UnlinkedDocument("orders.charge-card"), CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var read = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (read.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, clock);
            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);

            unlinked.Select(document => document.Action)
                .Should()
                .Equal("orders.place-order", "orders.reserve-stock", "orders.charge-card");
            unlinked.Should().OnlyContain(document => document.Segment == segment);
        }
    }

    [SkippableFact]
    public async Task ListUnlinkedAsync_with_an_age_returns_only_older_documents()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("older");
        var clock = new ManualClock(OpenedAt);
        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, clock);

            await store.StageAsync(business, segment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        clock.Advance(TimeSpan.FromMinutes(10));

        var later = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (later.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, clock);

            await store.StageAsync(later, segment, UnlinkedDocument("orders.reserve-stock"), CancellationToken.None).ConfigureAwait(false);

            await later.SaveChangesAsync().ConfigureAwait(false);
        }

        var read = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (read.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, clock);
            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, TimeSpan.FromMinutes(5), CancellationToken.None))
                .ConfigureAwait(false);

            unlinked.Select(document => document.Action).Should().Equal("orders.place-order");
        }
    }

    [SkippableFact]
    public async Task Staged_documents_roll_back_with_the_business_session()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("rollback");

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            await store.StageAsync(business, segment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);
        }

        var read = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (read.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);

            unlinked.Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task AppendAsync_removes_the_staged_entries_of_the_documents_it_links()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("linked");
        var first = UnlinkedDocument("orders.place-order");
        var second = UnlinkedDocument("orders.reserve-stock");

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            await store.StageAsync(business, segment, first, CancellationToken.None).ConfigureAwait(false);
            await store.StageAsync(business, segment, second, CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var link = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (link.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var expected = InitialHead(segment, "tenant-a");
            first.Sequence = 1;
            first.PreviousHash = null;
            first.Hash = "hash-1";
            first.HashScheme = "sha256-v1";
            second.Sequence = 2;
            second.PreviousHash = "hash-1";
            second.Hash = "hash-2";
            second.HashScheme = "sha256-v1";
            var updated = expected with { LastSequence = 2, LastHash = "hash-2", Count = 2, Version = 1 };

            await store
                .AppendAsync(segment, expected, [first, second], updated, CancellationToken.None)
                .ConfigureAwait(false);

            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);

            unlinked.Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task Staging_the_same_document_twice_keeps_one_document_and_one_entry()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("redelivery");
        var document = UnlinkedDocument("orders.place-order");

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            await store.StageAsync(business, segment, document, CancellationToken.None).ConfigureAwait(false);
            await store.StageAsync(business, segment, document, CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var read = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (read.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);

            unlinked.Should().ContainSingle();
        }
    }

    [SkippableFact]
    public async Task ReadSegmentAsync_pages_by_sequence_in_ascending_order()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("paging");
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var expected = InitialHead(segment, "tenant-a");
            MartenAuditRecord[] documents =
            [
                LinkedDocument(segment, 1, null, "hash-1"),
                LinkedDocument(segment, 2, "hash-1", "hash-2"),
                LinkedDocument(segment, 3, "hash-2", "hash-3"),
                LinkedDocument(segment, 4, "hash-3", "hash-4"),
                LinkedDocument(segment, 5, "hash-4", "hash-5")
            ];
            var updated = expected with { LastSequence = 5, LastHash = "hash-5", Count = 5, Version = 1 };

            await store
                .AppendAsync(segment, expected, documents, updated, CancellationToken.None)
                .ConfigureAwait(false);

            var middle = await CollectAsync(
                    store.ReadSegmentAsync(segment, 2, 4, CancellationToken.None))
                .ConfigureAwait(false);
            middle.Select(document => document.Sequence).Should().Equal(2L, 3L, 4L);

            var tail = await CollectAsync(
                    store.ReadSegmentAsync(segment, 4, null, CancellationToken.None))
                .ConfigureAwait(false);
            tail.Select(document => document.Sequence).Should().Equal(4L, 5L);

            var beyond = await CollectAsync(
                    store.ReadSegmentAsync(segment, 6, null, CancellationToken.None))
                .ConfigureAwait(false);
            beyond.Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task ListHeadsAsync_filters_by_tenant_attributes_age_and_expiry()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var tenant = $"tenant-{Guid.NewGuid():N}";
        var first = UniqueSegment("heads-first");
        var second = UniqueSegment("heads-second");
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var firstHead = new AuditSegmentHead(
                new AuditSegmentInfo(
                    first,
                    tenant,
                    new Dictionary<string, string> { ["class"] = "orders", ["year"] = "2026" },
                    OpenedAt),
                1,
                "hash-1",
                1,
                null,
                null,
                false,
                1);
            var secondHead = new AuditSegmentHead(
                new AuditSegmentInfo(
                    second,
                    tenant,
                    new Dictionary<string, string> { ["class"] = "ledger", ["year"] = "2026" },
                    OpenedAt.AddDays(1)),
                1,
                "hash-1",
                1,
                null,
                null,
                false,
                1);

            await store
                .AppendAsync(first, InitialHead(first, tenant), [LinkedDocument(first, 1, null, "hash-1")], firstHead, CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .AppendAsync(second, InitialHead(second, tenant), [LinkedDocument(second, 1, null, "hash-1")], secondHead, CancellationToken.None)
                .ConfigureAwait(false);

            var byTenant = await CollectAsync(
                    store.ListHeadsAsync(new AuditSegmentFilter(TenantId: tenant), CancellationToken.None))
                .ConfigureAwait(false);
            byTenant.Select(head => head.Info.Key).Should().BeEquivalentTo(first, second);

            var byAttribute = await CollectAsync(
                    store.ListHeadsAsync(
                        new AuditSegmentFilter(
                            TenantId: tenant,
                            Attributes: new Dictionary<string, string> { ["class"] = "ledger" }),
                        CancellationToken.None))
                .ConfigureAwait(false);
            byAttribute.Select(head => head.Info.Key).Should().Equal(second);

            var byAge = await CollectAsync(
                    store.ListHeadsAsync(
                        new AuditSegmentFilter(TenantId: tenant, OpenedBefore: OpenedAt.AddHours(12)),
                        CancellationToken.None))
                .ConfigureAwait(false);
            byAge.Select(head => head.Info.Key).Should().Equal(first);

            var expired = secondHead with { Expired = true, Version = 2 };

            await store
                .AppendAsync(second, secondHead, [], expired, CancellationToken.None)
                .ConfigureAwait(false);

            var open = await CollectAsync(
                    store.ListHeadsAsync(new AuditSegmentFilter(TenantId: tenant), CancellationToken.None))
                .ConfigureAwait(false);
            open.Select(head => head.Info.Key).Should().Equal(first);

            var withExpired = await CollectAsync(
                    store.ListHeadsAsync(
                        new AuditSegmentFilter(TenantId: tenant, IncludeExpired: true),
                        CancellationToken.None))
                .ConfigureAwait(false);
            withExpired.Select(head => head.Info.Key).Should().BeEquivalentTo(first, second);
        }
    }

    [SkippableFact]
    public async Task Checkpoints_store_read_list_and_move_through_publish_state()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("checkpoints");
        var checkpoint = new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            2,
            2,
            "hash-2",
            "sha256-v1",
            AuditCheckpointKind.Regular,
            OpenedAt)
        {
            Label = "nightly",
            PublishState = CheckpointPublishState.Pending,
            Signatures = [new AuditCheckpointSignature("hmac-sha256", "signature-value")]
        };
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

            var read = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            read.Should().NotBeNull();
            read!.Id.Should().Be(checkpoint.Id);
            read.Segment.Should().Be(segment);
            read.Sequence.Should().Be(2);
            read.Count.Should().Be(2);
            read.Hash.Should().Be("hash-2");
            read.HashScheme.Should().Be("sha256-v1");
            read.Kind.Should().Be(AuditCheckpointKind.Regular);
            read.CreatedAt.Should().Be(OpenedAt);
            read.Label.Should().Be("nightly");
            read.PublishState.Should().Be(CheckpointPublishState.Pending);
            read.Signatures.Should().BeEquivalentTo(checkpoint.Signatures);

            var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
            unpublished.Should().Contain(single => single.Id == checkpoint.Id);

            await store.MarkPublishedAsync(checkpoint.Id, "the anchor refused the connection", CancellationToken.None).ConfigureAwait(false);

            var failed = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            failed!.PublishState.Should().Be(CheckpointPublishState.Pending);
            failed.PublishAttempts.Should().Be(1);
            failed.LastPublishError.Should().Be("the anchor refused the connection");

            var stillUnpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
            stillUnpublished.Should().Contain(single => single.Id == checkpoint.Id);

            await store.MarkPublishedAsync(checkpoint.Id, null, CancellationToken.None).ConfigureAwait(false);

            var published = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            published!.PublishState.Should().Be(CheckpointPublishState.Published);
            published.PublishAttempts.Should().Be(2);
            published.LastPublishError.Should().BeNull();

            var afterPublish = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
            afterPublish.Should().NotContain(single => single.Id == checkpoint.Id);
        }
    }

    [SkippableFact]
    public async Task MarkPublishedAsync_for_an_unknown_checkpoint_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var checkpointId = Guid.NewGuid();

            var mark = () => store.MarkPublishedAsync(checkpointId, null, CancellationToken.None).AsTask();

            var thrown = await mark.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage($"*{checkpointId}*");
        }
    }

    [SkippableFact]
    public async Task GetLatestAsync_returns_the_highest_sequence_checkpoint()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var segment = UniqueSegment("latest");
        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(Guid.NewGuid(), segment, 4, 4, "hash-4", "sha256-v1", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(Guid.NewGuid(), segment, 2, 2, "hash-2", "sha256-v1", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);

            var latest = await store.GetLatestAsync(segment, CancellationToken.None).ConfigureAwait(false);
            latest!.Sequence.Should().Be(4);

            var missing = await store
                .GetLatestAsync($"nothing-{Guid.NewGuid():N}", CancellationToken.None)
                .ConfigureAwait(false);
            missing.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task EraseAsync_by_tenant_removes_only_that_tenant()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var erasedTenant = $"erased-{Guid.NewGuid():N}";
        var keptTenant = $"kept-{Guid.NewGuid():N}";
        var erasedSegment = UniqueSegment("erased");
        var stagedOnlySegment = UniqueSegment("erased-staged-only");
        var keptSegment = UniqueSegment("kept");

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
            var erasedHead = InitialHead(erasedSegment, erasedTenant);
            var keptHead = InitialHead(keptSegment, keptTenant);

            await store
                .AppendAsync(
                    erasedSegment,
                    erasedHead,
                    [LinkedDocument(erasedSegment, 1, null, "hash-1")],
                    erasedHead with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .AppendAsync(
                    keptSegment,
                    keptHead,
                    [LinkedDocument(keptSegment, 1, null, "hash-1")],
                    keptHead with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(Guid.NewGuid(), erasedSegment, 1, 1, "hash-1", "sha256-v1", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);

            var staged = UnlinkedDocument("orders.place-order");
            staged.TenantId = erasedTenant;

            await store.StageAsync(business, erasedSegment, staged, CancellationToken.None).ConfigureAwait(false);

            var stagedOnly = UnlinkedDocument("orders.reserve-stock");
            stagedOnly.TenantId = erasedTenant;

            await store.StageAsync(business, stagedOnlySegment, stagedOnly, CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var erase = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (erase.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);

            await store
                .EraseAsync(new AuditSegmentFilter(TenantId: erasedTenant), CancellationToken.None)
                .ConfigureAwait(false);

            var erasedHead = await store.ReadHeadAsync(erasedSegment, CancellationToken.None).ConfigureAwait(false);
            erasedHead.Should().BeNull();

            var erasedDocuments = await CollectAsync(
                    store.ReadSegmentAsync(erasedSegment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            erasedDocuments.Should().BeEmpty();

            var erasedUnlinked = await CollectAsync(
                    store.ListUnlinkedAsync(erasedSegment, null, CancellationToken.None))
                .ConfigureAwait(false);
            erasedUnlinked.Should().BeEmpty();

            var stagedOnlyUnlinked = await CollectAsync(
                    store.ListUnlinkedAsync(stagedOnlySegment, null, CancellationToken.None))
                .ConfigureAwait(false);
            stagedOnlyUnlinked.Should().BeEmpty();

            var erasedCheckpoint = await store.GetLatestAsync(erasedSegment, CancellationToken.None).ConfigureAwait(false);
            erasedCheckpoint.Should().BeNull();

            var keptHead = await store.ReadHeadAsync(keptSegment, CancellationToken.None).ConfigureAwait(false);
            keptHead.Should().NotBeNull();

            var keptDocuments = await CollectAsync(
                    store.ReadSegmentAsync(keptSegment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            keptDocuments.Should().ContainSingle();
        }
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_auditing_store_tests";
        });
    }

    private static string UniqueSegment(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static AuditSegmentInfo SegmentInfo(string segment, string tenant)
    {
        return new AuditSegmentInfo(
            segment,
            tenant,
            new Dictionary<string, string>
            {
                ["category"] = "orders",
                ["year"] = "2026"
            },
            OpenedAt);
    }

    private static AuditSegmentHead InitialHead(string segment, string tenant)
    {
        return new AuditSegmentHead(SegmentInfo(segment, tenant), 0, null, 0, null, null, false, 0);
    }

    private static MartenAuditRecord LinkedDocument(string segment, long sequence, string? previousHash, string hash)
    {
        return new MartenAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = OpenedAt,
            Category = "orders",
            TenantId = "tenant-a",
            Segment = segment,
            Sequence = sequence,
            PreviousHash = previousHash,
            Hash = hash,
            HashScheme = "sha256-v1"
        };
    }

    private static MartenAuditRecord UnlinkedDocument(string action)
    {
        return new MartenAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = OpenedAt,
            Category = "orders",
            TenantId = "tenant-a"
        };
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualClock(DateTimeOffset start)
        {
            _utcNow = start;
        }

        public void Advance(TimeSpan step)
        {
            _utcNow += step;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
