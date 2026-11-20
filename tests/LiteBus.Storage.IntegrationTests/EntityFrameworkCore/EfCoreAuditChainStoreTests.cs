using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Auditing.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Integration tests for the Entity Framework Core audit chain store: heads with conditional versions,
///     staged documents in commit order, sequence paging, head filtering, checkpoint publish state, and
///     erasure.
/// </summary>
public sealed class EfCoreAuditChainStoreTests : IClassFixture<SqliteDatabaseFixture>, IDisposable
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture;
    private string? _customDatabasePath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditChainStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreAuditChainStoreTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReadHeadAsync_for_an_unknown_segment_returns_null()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();

            var head = await store
                .ReadHeadAsync($"unknown-{Guid.NewGuid():N}", CancellationToken.None)
                .ConfigureAwait(false);

            head.Should().BeNull();
        }
    }

    [Fact]
    public async Task AppendAsync_creates_the_head_and_stores_the_documents()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("append");
            var expected = InitialHead(segment, "tenant-a");
            EfCoreAuditRecord[] documents =
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
            head.LastCheckpointSequence.Should().BeNull();
            head.LastCheckpointAt.Should().BeNull();
            head.Expired.Should().BeFalse();
            head.Version.Should().Be(1);

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Select(document => document.Sequence).Should().Equal(1L, 2L);
            stored[0].Hash.Should().Be("hash-1");
            stored[1].Hash.Should().Be("hash-2");
            stored[0].PreviousHash.Should().BeNull();
            stored[1].PreviousHash.Should().Be("hash-1");
            stored.Should().OnlyContain(document => document.Segment == segment);
            stored.Should().OnlyContain(document => document.HashScheme == "sha256-v2");

            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);
            unlinked.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AppendAsync_with_a_stale_version_throws_a_head_conflict_and_stores_nothing()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("stale");
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

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().NotBeNull();
            head!.Version.Should().Be(1);
            head.LastHash.Should().Be("hash-1");

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Should().ContainSingle();
            stored[0].Hash.Should().Be("hash-1");
        }
    }

    [Fact]
    public async Task AppendAsync_after_a_conflict_succeeds_against_the_freshly_read_winner()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("retry");
            var first = InitialHead(segment, "tenant-a");
            var linked = first with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, first, [LinkedDocument(segment, 1, null, "hash-1")], linked, CancellationToken.None)
                .ConfigureAwait(false);

            var stale = () => store.AppendAsync(
                segment,
                first,
                [LinkedDocument(segment, 2, "hash-1", "hash-stale")],
                linked with { LastSequence = 2, LastHash = "hash-stale", Count = 2, Version = 2 },
                CancellationToken.None).AsTask();

            await stale.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false);

            // The failed append ran on a short context the store disposed, so the next read sees the stored
            // winner and a new append against it commits the remaining document.
            var winner = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            winner.Should().NotBeNull();
            var retried = winner! with { LastSequence = 2, LastHash = "hash-2", Count = 2, Version = 2 };

            await store
                .AppendAsync(segment, winner, [LinkedDocument(segment, 2, "hash-1", "hash-2")], retried, CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.Version.Should().Be(2);
            head.LastHash.Should().Be("hash-2");

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Select(document => document.Sequence).Should().Equal(1L, 2L);
        }
    }

    [Fact]
    public async Task AppendAsync_with_a_head_committed_by_another_context_throws_a_head_conflict()
    {
        var segment = UniqueSegment("cross-context");
        var first = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (first.ConfigureAwait(false))
        {
            var store = CreateStore();
            var expected = InitialHead(segment, "tenant-a");
            var updated = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], updated, CancellationToken.None)
                .ConfigureAwait(false);
        }

        var second = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (second.ConfigureAwait(false))
        {
            var store = CreateStore();
            var stale = InitialHead(segment, "tenant-a");

            var append = () => store.AppendAsync(
                segment,
                stale,
                [LinkedDocument(segment, 1, null, "hash-other")],
                stale with { LastSequence = 1, LastHash = "hash-other", Count = 1, Version = 1 },
                CancellationToken.None).AsTask();

            var thrown = await append.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false);
            thrown.WithMessage($"*{segment}*");

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.Version.Should().Be(1);
            head.LastHash.Should().Be("hash-1");
        }
    }

    [Fact]
    public async Task AppendAsync_with_a_head_moved_by_another_store_throws_a_head_conflict()
    {
        var segment = UniqueSegment("tracked-stale");
        var trailing = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (trailing.ConfigureAwait(false))
        {
            var trailingStore = CreateStore();
            var first = InitialHead(segment, "tenant-a");
            var linked = first with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await trailingStore
                .AppendAsync(segment, first, [LinkedDocument(segment, 1, null, "hash-1")], linked, CancellationToken.None)
                .ConfigureAwait(false);

            var leading = await CreatePreparedContextAsync().ConfigureAwait(false);
            await using (leading.ConfigureAwait(false))
            {
                var leadingStore = CreateStore();
                var winner = linked with { LastSequence = 2, LastHash = "hash-leading", Count = 2, Version = 2 };

                await leadingStore
                    .AppendAsync(segment, linked, [LinkedDocument(segment, 2, "hash-1", "hash-leading")], winner, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            // Each append reads the head on a fresh context, so the trailing writer's stale version fails
            // the store's explicit head check against the version the leading writer committed.
            var conflict = () => trailingStore.AppendAsync(
                segment,
                linked,
                [LinkedDocument(segment, 2, "hash-1", "hash-trailing")],
                linked with { LastSequence = 2, LastHash = "hash-trailing", Count = 2, Version = 2 },
                CancellationToken.None).AsTask();

            await conflict.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false);

            var head = await trailingStore.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.Version.Should().Be(2);
            head.LastHash.Should().Be("hash-leading");
        }
    }

    [Fact]
    public async Task AppendAsync_to_an_expired_head_throws()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("expired");
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

    [Fact]
    public async Task AppendAsync_with_no_documents_moves_the_head_only()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("head-only");
            var expected = InitialHead(segment, "tenant-a");
            var linked = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], linked, CancellationToken.None)
                .ConfigureAwait(false);

            var checkpointAt = OpenedAt.AddHours(1);
            var checkpointed = linked with
            {
                LastCheckpointSequence = 1,
                LastCheckpointAt = checkpointAt,
                Version = 2
            };

            await store
                .AppendAsync(segment, linked, [], checkpointed, CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().NotBeNull();
            head!.Version.Should().Be(2);
            head.LastSequence.Should().Be(1);
            head.Count.Should().Be(1);
            head.LastCheckpointSequence.Should().Be(1);
            head.LastCheckpointAt.Should().Be(checkpointAt);

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Staged_documents_list_in_commit_order()
    {
        var segment = UniqueSegment("staged");
        var clock = new ManualClock(OpenedAt);

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore(clock);

            await store.StageAsync(business, segment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(1));
            await store.StageAsync(business, segment, UnlinkedDocument("orders.reserve-stock"), CancellationToken.None).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(1));
            await store.StageAsync(business, segment, UnlinkedDocument("orders.charge-card"), CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var read = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            var store = CreateStore(clock);
            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);

            unlinked.Select(document => document.Action)
                .Should()
                .Equal("orders.place-order", "orders.reserve-stock", "orders.charge-card");
            unlinked.Should().OnlyContain(document => document.Segment == segment);
            unlinked.Should().OnlyContain(document => document.Sequence == 0);
        }
    }

    [Fact]
    public async Task Staging_writes_a_placeholder_head_that_the_first_append_promotes()
    {
        var segment = UniqueSegment("placeholder");
        var clock = new ManualClock(OpenedAt);

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore(clock);

            await store.StageAsync(business, segment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);
            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(clock);

            var placeholder = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            placeholder.Should().NotBeNull();
            placeholder!.Info.Key.Should().Be(segment);
            placeholder.Info.TenantId.Should().Be("tenant-a");
            placeholder.Info.Attributes.Should().BeEmpty();
            placeholder.LastSequence.Should().Be(0);
            placeholder.Count.Should().Be(0);
            placeholder.Version.Should().Be(0);

            var updated = InitialHead(segment, "tenant-a") with
            {
                LastSequence = 1,
                LastHash = "hash-1",
                Count = 1,
                Version = 1
            };

            await store
                .AppendAsync(segment, placeholder!, [LinkedDocument(segment, 1, null, "hash-1")], updated, CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.Info.Attributes.Should().BeEquivalentTo(updated.Info.Attributes);
            head.LastSequence.Should().Be(1);
            head.Version.Should().Be(1);
        }
    }

    [Fact]
    public async Task Staged_documents_roll_back_with_the_business_context()
    {
        var segment = UniqueSegment("rollback");

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store.StageAsync(business, segment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);
        }

        var read = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            var store = CreateStore();

            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);
            unlinked.Should().BeEmpty();

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().BeNull();
        }
    }

    [Fact]
    public async Task AppendAsync_links_staged_documents_in_place()
    {
        var segment = UniqueSegment("linked");
        var first = UnlinkedDocument("orders.place-order");
        var second = UnlinkedDocument("orders.reserve-stock");

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store.StageAsync(business, segment, first, CancellationToken.None).ConfigureAwait(false);
            await store.StageAsync(business, segment, second, CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var link = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (link.ConfigureAwait(false))
        {
            var store = CreateStore();
            var pending = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);
            pending.Should().HaveCount(2);

            pending[0].Sequence = 1;
            pending[0].PreviousHash = null;
            pending[0].Hash = "hash-1";
            pending[0].HashScheme = "sha256-v2";
            pending[1].Sequence = 2;
            pending[1].PreviousHash = "hash-1";
            pending[1].Hash = "hash-2";
            pending[1].HashScheme = "sha256-v2";

            var placeholder = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            placeholder.Should().NotBeNull();
            var updated = InitialHead(segment, "tenant-a") with
            {
                LastSequence = 2,
                LastHash = "hash-2",
                Count = 2,
                Version = 1
            };

            await store
                .AppendAsync(segment, placeholder!, pending, updated, CancellationToken.None)
                .ConfigureAwait(false);

            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);
            unlinked.Should().BeEmpty();

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Select(document => document.Sequence).Should().Equal(1L, 2L);
        }
    }

    [Fact]
    public async Task Staging_the_same_document_twice_keeps_one_document()
    {
        var segment = UniqueSegment("redelivery");
        var document = UnlinkedDocument("orders.place-order");

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store.StageAsync(business, segment, document, CancellationToken.None).ConfigureAwait(false);
            await store.StageAsync(business, segment, document, CancellationToken.None).ConfigureAwait(false);

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var restage = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (restage.ConfigureAwait(false))
        {
            var store = CreateStore();
            var redelivered = UnlinkedDocument("orders.place-order");
            redelivered.Id = document.Id;

            await store.StageAsync(restage, segment, redelivered, CancellationToken.None).ConfigureAwait(false);
            await restage.SaveChangesAsync().ConfigureAwait(false);

            var unlinked = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);
            unlinked.Should().ContainSingle();
            unlinked[0].Id.Should().Be(document.Id);
        }
    }

    [Fact]
    public async Task StageAsync_for_a_document_with_a_chain_position_throws()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("linked-stage");

            var stage = () => store.StageAsync(context, segment, LinkedDocument(segment, 1, null, "hash-1"), CancellationToken.None).AsTask();

            var thrown = await stage.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*chain position*");
        }
    }

    [Fact]
    public async Task StageAsync_without_audit_mapping_throws_a_configuration_error()
    {
        var options = new DbContextOptionsBuilder<UnmappedAuditDbContext>()
            .UseSqlite(_fixture.ConnectionString)
            .Options;
        var context = new UnmappedAuditDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var store = new EfCoreAuditChainStore<EfCoreAuditRecord>(
                new EfCoreAuditTestContextFactory(() => new UnmappedAuditDbContext(options)));

            var stage = () => store.StageAsync(context, UniqueSegment("unmapped"), UnlinkedDocument("orders.place-order"), CancellationToken.None).AsTask();

            var thrown = await stage.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*GetModelBuilderConfiguration*");
        }
    }

    [Fact]
    public async Task ReadSegmentAsync_pages_by_sequence_in_ascending_order()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("paging");
            var expected = InitialHead(segment, "tenant-a");
            EfCoreAuditRecord[] documents =
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

            var unknown = await CollectAsync(
                    store.ReadSegmentAsync($"unknown-{Guid.NewGuid():N}", 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            unknown.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ReadSegmentAsync_with_a_reversed_range_throws()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();

            var read = async () =>
            {
                await CollectAsync(
                    store.ReadSegmentAsync(UniqueSegment("reversed"), 4, 2, CancellationToken.None)).ConfigureAwait(false);
            };

            await read.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ListHeadsAsync_filters_by_tenant_attributes_age_and_expiry()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var tenant = $"tenant-{Guid.NewGuid():N}";
            var first = UniqueSegment("heads-first");
            var second = UniqueSegment("heads-second");
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

    [Fact]
    public async Task Checkpoints_store_read_list_and_move_through_publish_state()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("checkpoints");
            var checkpoint = new AuditCheckpoint(
                Guid.NewGuid(),
                segment,
                2,
                2,
                "hash-2",
                "sha256-v2",
                AuditCheckpointKind.Regular,
                OpenedAt)
            {
                Label = "nightly",
                PublishState = CheckpointPublishState.Pending,
                Signatures = [new AuditCheckpointSignature("hmac-sha256", "signature-value")]
            };

            await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

            var read = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            read.Should().NotBeNull();
            read!.Id.Should().Be(checkpoint.Id);
            read.Segment.Should().Be(segment);
            read.Sequence.Should().Be(2);
            read.Count.Should().Be(2);
            read.Hash.Should().Be("hash-2");
            read.HashScheme.Should().Be("sha256-v2");
            read.Kind.Should().Be(AuditCheckpointKind.Regular);
            read.CreatedAt.Should().Be(OpenedAt);
            read.Label.Should().Be("nightly");
            read.PublishState.Should().Be(CheckpointPublishState.Pending);
            read.PublishAttempts.Should().Be(0);
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

    [Fact]
    public async Task StoreCheckpointAsync_round_trips_a_single_signature_and_a_final_kind()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var checkpoint = new AuditCheckpoint(
                Guid.NewGuid(),
                UniqueSegment("signatures"),
                1,
                1,
                "hash-1",
                "sha256-v2",
                AuditCheckpointKind.Final,
                OpenedAt)
            {
                Signature = new AuditCheckpointSignature("hmac-sha256", "signature-1"),
                Label = "hourly",
                PublishState = CheckpointPublishState.Pending,
                Signatures =
                [
                    new AuditCheckpointSignature("hmac-sha256", "signature-1"),
                    new AuditCheckpointSignature("rfc3161", "token-1")
                ]
            };

            await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

            var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            stored.Should().NotBeNull();
            stored!.Kind.Should().Be(AuditCheckpointKind.Final);
            stored.Label.Should().Be("hourly");
            stored.Signature.Should().Be(checkpoint.Signature);
            stored.Signatures.Should().BeEquivalentTo(checkpoint.Signatures);

            var unknown = await store.ReadCheckpointAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false);
            unknown.Should().BeNull();
        }
    }

    [Fact]
    public async Task StoreCheckpointAsync_with_identical_content_is_a_noop()
    {
        var segment = UniqueSegment("checkpoint-noop");
        var checkpoint = new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            1,
            1,
            "hash-1",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            OpenedAt)
        {
            PublishState = CheckpointPublishState.Pending
        };

        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
            await store.StoreCheckpointAsync(checkpoint with { }, CancellationToken.None).ConfigureAwait(false);
        }

        var retry = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (retry.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store.StoreCheckpointAsync(checkpoint with { }, CancellationToken.None).ConfigureAwait(false);

            var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            stored.Should().NotBeNull();
            stored!.Hash.Should().Be("hash-1");
        }
    }

    [Fact]
    public async Task StoreCheckpointAsync_with_different_content_throws()
    {
        var segment = UniqueSegment("checkpoint-conflict");
        var checkpoint = new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            1,
            1,
            "hash-1",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            OpenedAt)
        {
            PublishState = CheckpointPublishState.Pending
        };

        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        }

        var retry = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (retry.ConfigureAwait(false))
        {
            var store = CreateStore();
            var changed = checkpoint with { Hash = "hash-changed" };

            var storeAgain = () => store.StoreCheckpointAsync(changed, CancellationToken.None).AsTask();

            var thrown = await storeAgain.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage($"*{checkpoint.Id}*");

            var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            stored!.Hash.Should().Be("hash-1");
        }
    }

    [Fact]
    public async Task MarkPublishedAsync_for_an_unknown_checkpoint_is_a_noop()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var unknownId = Guid.NewGuid();

            await store.MarkPublishedAsync(unknownId, null, CancellationToken.None).ConfigureAwait(false);

            var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
            unpublished.Select(checkpoint => checkpoint.Id).Should().NotContain(unknownId);
        }
    }

    [Fact]
    public async Task GetLatestAsync_returns_the_highest_sequence_checkpoint()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("latest");

            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(Guid.NewGuid(), segment, 4, 4, "hash-4", "sha256-v2", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(Guid.NewGuid(), segment, 2, 2, "hash-2", "sha256-v2", AuditCheckpointKind.Regular, OpenedAt),
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

    [Fact]
    public async Task EraseAsync_by_tenant_removes_only_that_tenant()
    {
        var erasedTenant = $"erased-{Guid.NewGuid():N}";
        var keptTenant = $"kept-{Guid.NewGuid():N}";
        var erasedSegment = UniqueSegment("erased");
        var keptSegment = UniqueSegment("kept");
        var erasedCheckpointId = Guid.NewGuid();
        var keptCheckpointId = Guid.NewGuid();

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();
            var erasedHead = InitialHead(erasedSegment, erasedTenant);
            var keptHead = InitialHead(keptSegment, keptTenant);

            await store
                .AppendAsync(
                    erasedSegment,
                    erasedHead,
                    [LinkedDocument(erasedSegment, 1, null, "hash-1"), LinkedDocument(erasedSegment, 2, "hash-1", "hash-2")],
                    erasedHead with { LastSequence = 2, LastHash = "hash-2", Count = 2, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .AppendAsync(
                    keptSegment,
                    keptHead,
                    [LinkedDocument(keptSegment, 1, null, "hash-1"), LinkedDocument(keptSegment, 2, "hash-1", "hash-2")],
                    keptHead with { LastSequence = 2, LastHash = "hash-2", Count = 2, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);

            await store.StageAsync(business, erasedSegment, UnlinkedDocument("orders.place-order"), CancellationToken.None).ConfigureAwait(false);
            await store.StageAsync(business, keptSegment, UnlinkedDocument("orders.reserve-stock"), CancellationToken.None).ConfigureAwait(false);
            await business.SaveChangesAsync().ConfigureAwait(false);

            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(erasedCheckpointId, erasedSegment, 2, 2, "hash-2", "sha256-v2", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(keptCheckpointId, keptSegment, 2, 2, "hash-2", "sha256-v2", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        var erase = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (erase.ConfigureAwait(false))
        {
            var store = CreateStore();

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

            var erasedCheckpoint = await store.ReadCheckpointAsync(erasedCheckpointId, CancellationToken.None).ConfigureAwait(false);
            erasedCheckpoint.Should().BeNull();

            var keptHead = await store.ReadHeadAsync(keptSegment, CancellationToken.None).ConfigureAwait(false);
            keptHead.Should().NotBeNull();
            keptHead!.Count.Should().Be(2);

            var keptDocuments = await CollectAsync(
                    store.ReadSegmentAsync(keptSegment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            keptDocuments.Should().HaveCount(2);

            var keptUnlinked = await CollectAsync(
                    store.ListUnlinkedAsync(keptSegment, null, CancellationToken.None))
                .ConfigureAwait(false);
            keptUnlinked.Should().ContainSingle();

            var keptCheckpoint = await store.ReadCheckpointAsync(keptCheckpointId, CancellationToken.None).ConfigureAwait(false);
            keptCheckpoint.Should().NotBeNull();
            keptCheckpoint!.PublishState.Should().Be(CheckpointPublishState.Pending);

            var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
            unpublished.Select(checkpoint => checkpoint.Id).Should().Contain(keptCheckpointId);
            unpublished.Select(checkpoint => checkpoint.Id).Should().NotContain(erasedCheckpointId);
        }
    }

    [Fact]
    public async Task EraseAsync_removes_staged_only_segments_of_the_tenant()
    {
        var erasedTenant = $"erased-{Guid.NewGuid():N}";
        var keptTenant = $"kept-{Guid.NewGuid():N}";
        var erasedSegment = UniqueSegment("erased-staged-only");
        var keptSegment = UniqueSegment("kept-staged-only");

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();
            var erased = UnlinkedDocument("orders.place-order");
            erased.TenantId = erasedTenant;
            var kept = UnlinkedDocument("orders.reserve-stock");
            kept.TenantId = keptTenant;

            await store.StageAsync(business, erasedSegment, erased, CancellationToken.None).ConfigureAwait(false);
            await store.StageAsync(business, keptSegment, kept, CancellationToken.None).ConfigureAwait(false);
            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var erase = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (erase.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store
                .EraseAsync(new AuditSegmentFilter(TenantId: erasedTenant), CancellationToken.None)
                .ConfigureAwait(false);

            var erasedHead = await store.ReadHeadAsync(erasedSegment, CancellationToken.None).ConfigureAwait(false);
            erasedHead.Should().BeNull();

            var erasedUnlinked = await CollectAsync(
                    store.ListUnlinkedAsync(erasedSegment, null, CancellationToken.None))
                .ConfigureAwait(false);
            erasedUnlinked.Should().BeEmpty();

            var keptHead = await store.ReadHeadAsync(keptSegment, CancellationToken.None).ConfigureAwait(false);
            keptHead.Should().NotBeNull();

            var keptUnlinked = await CollectAsync(
                    store.ListUnlinkedAsync(keptSegment, null, CancellationToken.None))
                .ConfigureAwait(false);
            keptUnlinked.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task EraseAsync_ignores_whether_segments_expired()
    {
        var tenant = $"tenant-{Guid.NewGuid():N}";
        var segment = UniqueSegment("erased-expired");

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();
            var expected = InitialHead(segment, tenant);
            var linked = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], linked, CancellationToken.None)
                .ConfigureAwait(false);

            await store
                .AppendAsync(segment, linked, [], linked with { Expired = true, Version = 2 }, CancellationToken.None)
                .ConfigureAwait(false);
        }

        var erase = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (erase.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store
                .EraseAsync(new AuditSegmentFilter(TenantId: tenant), CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().BeNull();

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task EraseAsync_by_attributes_removes_only_matching_segments()
    {
        var erasedSegment = UniqueSegment("erased-tax");
        var keptSegment = UniqueSegment("kept-operations");

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();
            var erasedInfo = new AuditSegmentInfo(
                erasedSegment,
                null,
                new Dictionary<string, string> { ["class"] = "tax" },
                OpenedAt);
            var erased = new AuditSegmentHead(erasedInfo, 0, null, 0, null, null, false, 0);
            var keptInfo = new AuditSegmentInfo(
                keptSegment,
                null,
                new Dictionary<string, string> { ["class"] = "ops" },
                OpenedAt);
            var kept = new AuditSegmentHead(keptInfo, 0, null, 0, null, null, false, 0);

            await store
                .AppendAsync(
                    erasedSegment,
                    erased,
                    [LinkedDocument(erasedSegment, 1, null, "hash-1")],
                    erased with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .AppendAsync(
                    keptSegment,
                    kept,
                    [LinkedDocument(keptSegment, 1, null, "hash-1")],
                    kept with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        var erase = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (erase.ConfigureAwait(false))
        {
            var store = CreateStore();
            var filter = new Dictionary<string, string> { ["class"] = "tax" };

            await store
                .EraseAsync(new AuditSegmentFilter(Attributes: filter), CancellationToken.None)
                .ConfigureAwait(false);

            var erasedHead = await store.ReadHeadAsync(erasedSegment, CancellationToken.None).ConfigureAwait(false);
            erasedHead.Should().BeNull();

            var erasedDocuments = await CollectAsync(
                    store.ReadSegmentAsync(erasedSegment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            erasedDocuments.Should().BeEmpty();

            var keptHead = await store.ReadHeadAsync(keptSegment, CancellationToken.None).ConfigureAwait(false);
            keptHead.Should().NotBeNull();

            var keptDocuments = await CollectAsync(
                    store.ReadSegmentAsync(keptSegment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            keptDocuments.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task EraseAsync_with_a_filter_matching_nothing_changes_nothing()
    {
        var segment = UniqueSegment("erase-nothing");
        var checkpointId = Guid.NewGuid();

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateStore();
            var expected = InitialHead(segment, "tenant-a");
            var updated = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], updated, CancellationToken.None)
                .ConfigureAwait(false);
            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(checkpointId, segment, 1, 1, "hash-1", "sha256-v2", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        var erase = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (erase.ConfigureAwait(false))
        {
            var store = CreateStore();

            await store
                .EraseAsync(new AuditSegmentFilter(TenantId: $"nobody-{Guid.NewGuid():N}"), CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().NotBeNull();
            head!.Count.Should().Be(1);

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Should().ContainSingle();

            var checkpoint = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
            checkpoint.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ReadHeadAsync_with_a_cancelled_token_throws()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            var read = () => store.ReadHeadAsync(UniqueSegment("cancelled"), cancellationSource.Token).AsTask();

            await read.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task AppendAsync_with_a_cancelled_token_stores_nothing()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("cancelled-append");
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            var expected = InitialHead(segment, "tenant-a");
            var updated = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            var append = () => store.AppendAsync(
                segment,
                expected,
                [LinkedDocument(segment, 1, null, "hash-1")],
                updated,
                cancellationSource.Token).AsTask();

            await append.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().BeNull();
        }
    }

    [Fact]
    public async Task AppendAsync_with_a_document_of_another_segment_throws()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("segment");
            var expected = InitialHead(segment, "tenant-a");

            var append = () => store.AppendAsync(
                segment,
                expected,
                [LinkedDocument($"other-{Guid.NewGuid():N}", 1, null, "hash-1")],
                expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 },
                CancellationToken.None).AsTask();

            var thrown = await append.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*other*");
        }
    }

    [Fact]
    public async Task AppendAsync_assigns_documents_without_a_segment()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore();
            var segment = UniqueSegment("assigned");
            var expected = InitialHead(segment, "tenant-a");
            var document = UnlinkedDocument("orders.place-order");
            document.Sequence = 1;
            document.Hash = "hash-1";
            document.HashScheme = "sha256-v2";

            await store
                .AppendAsync(
                    segment,
                    expected,
                    [document],
                    expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 },
                    CancellationToken.None)
                .ConfigureAwait(false);

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Should().ContainSingle();
            stored[0].Segment.Should().Be(segment);
        }
    }

    [Fact]
    public async Task Custom_documents_stage_link_and_read_through_the_generic_mapping()
    {
        var segment = UniqueSegment("custom");
        var clock = new ManualClock(OpenedAt);

        var business = await CreatePreparedCustomContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = CreateCustomStore(clock);
            var first = new CustomAuditDocument { Id = Guid.NewGuid(), Marker = "marker-1" };

            await store.StageAsync(business, segment, first, CancellationToken.None).ConfigureAwait(false);
            business.Entry(first).Property("StagedAt").CurrentValue.Should().Be(OpenedAt);

            clock.Advance(TimeSpan.FromSeconds(1));
            var second = new CustomAuditDocument { Id = Guid.NewGuid(), Marker = "marker-2" };

            await store.StageAsync(business, segment, second, CancellationToken.None).ConfigureAwait(false);
            business.Entry(second).Property("StagedAt").CurrentValue.Should().Be(OpenedAt.AddSeconds(1));

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var link = await CreatePreparedCustomContextAsync().ConfigureAwait(false);
        await using (link.ConfigureAwait(false))
        {
            var store = CreateCustomStore(clock);
            var pending = await CollectAsync(
                    store.ListUnlinkedAsync(segment, null, CancellationToken.None))
                .ConfigureAwait(false);
            pending.Select(document => document.Marker).Should().Equal("marker-1", "marker-2");

            pending[0].Sequence = 1;
            pending[0].PreviousHash = null;
            pending[0].Hash = "hash-1";
            pending[0].HashScheme = "sha256-v2";
            pending[1].Sequence = 2;
            pending[1].PreviousHash = "hash-1";
            pending[1].Hash = "hash-2";
            pending[1].HashScheme = "sha256-v2";

            var placeholder = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            placeholder.Should().NotBeNull();
            var updated = InitialHead(segment, "tenant-a") with
            {
                LastSequence = 2,
                LastHash = "hash-2",
                Count = 2,
                Version = 1
            };

            await store
                .AppendAsync(segment, placeholder!, pending, updated, CancellationToken.None)
                .ConfigureAwait(false);

            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);
            stored.Select(document => document.Marker).Should().Equal("marker-1", "marker-2");

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.Version.Should().Be(1);
            head.Count.Should().Be(2);
        }
    }

    /// <summary>
    ///     Deletes the custom audit database file backing the custom document test.
    /// </summary>
    public void Dispose()
    {
        if (_customDatabasePath is not null && File.Exists(_customDatabasePath))
        {
            File.Delete(_customDatabasePath);
        }
    }

    /// <summary>
    ///     Builds a chain store whose operations open fresh contexts on the fixture database.
    /// </summary>
    /// <param name="clock">The clock the store stamps staged documents with, or <see langword="null" /> for the system clock.</param>
    /// <returns>The store.</returns>
    private EfCoreAuditChainStore<EfCoreAuditRecord> CreateStore(TimeProvider? clock = null)
    {
        return new EfCoreAuditChainStore<EfCoreAuditRecord>(
            new EfCoreAuditTestContextFactory(() =>
            {
                var context = CreateContext();
                context.Database.EnsureCreated();
                return context;
            }),
            clock);
    }

    /// <summary>
    ///     Builds a chain store for the custom document whose operations open fresh contexts on the custom database.
    /// </summary>
    /// <param name="clock">The clock the store stamps staged documents with, or <see langword="null" /> for the system clock.</param>
    /// <returns>The store.</returns>
    private EfCoreAuditChainStore<CustomAuditDocument> CreateCustomStore(TimeProvider? clock = null)
    {
        return new EfCoreAuditChainStore<CustomAuditDocument>(
            new EfCoreAuditTestContextFactory(() =>
            {
                var context = CreateCustomContext();
                context.Database.EnsureCreated();
                return context;
            }),
            clock);
    }

    /// <summary>
    ///     Creates an audit test context over the fixture database without creating the schema.
    /// </summary>
    /// <returns>The context.</returns>
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

    /// <summary>
    ///     Creates an audit test context over the fixture database with the audit tables created.
    /// </summary>
    /// <returns>The context.</returns>
    private async Task<EfCoreAuditTestDbContext> CreatePreparedContextAsync()
    {
        var context = CreateContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return context;
    }

    /// <summary>
    ///     Creates a context over the custom document database without creating the schema.
    /// </summary>
    /// <returns>The context.</returns>
    private CustomAuditDbContext CreateCustomContext()
    {
        // The custom document model needs its own tables, and EnsureCreated creates nothing when the
        // database file already exists. The fixture file already holds the audit tables by the time this
        // test runs, so the custom contexts share a database file of their own where EnsureCreated
        // creates the full custom schema.
        _customDatabasePath ??= Path.Combine(Path.GetTempPath(), $"litebus-audit-custom-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _customDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
            Pooling = false
        }.ToString();
        return new CustomAuditDbContext(
            new DbContextOptionsBuilder<CustomAuditDbContext>()
                .UseSqlite(connectionString)
                .Options);
    }

    /// <summary>
    ///     Creates a context over the custom document database with the audit tables created.
    /// </summary>
    /// <returns>The context.</returns>
    private async Task<CustomAuditDbContext> CreatePreparedCustomContextAsync()
    {
        var context = CreateCustomContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return context;
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

    private static EfCoreAuditRecord LinkedDocument(string segment, long sequence, string? previousHash, string hash)
    {
        return new EfCoreAuditRecord
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
            HashScheme = "sha256-v2"
        };
    }

    private static EfCoreAuditRecord UnlinkedDocument(string action)
    {
        return new EfCoreAuditRecord
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

    private sealed class UnmappedAuditDbContext : DbContext, IAuditDbContext
    {
        public UnmappedAuditDbContext(DbContextOptions<UnmappedAuditDbContext> options)
            : base(options)
        {
        }

        public DbSet<AuditSegmentHeadEntity> AuditSegmentHeads => Set<AuditSegmentHeadEntity>();

        public DbSet<AuditCheckpointEntity> AuditCheckpoints => Set<AuditCheckpointEntity>();
    }

    private sealed class CustomAuditDocument : IAuditChainDocument
    {
        public Guid Id { get; set; }

        public string Marker { get; set; } = string.Empty;

        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }
    }

    private sealed class CustomAuditDbContext : DbContext, IAuditDbContext
    {
        public CustomAuditDbContext(DbContextOptions<CustomAuditDbContext> options)
            : base(options)
        {
        }

        public DbSet<AuditSegmentHeadEntity> AuditSegmentHeads => Set<AuditSegmentHeadEntity>();

        public DbSet<AuditCheckpointEntity> AuditCheckpoints => Set<AuditCheckpointEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration<CustomAuditDocument>(
                new EntityFrameworkCoreAuditStoreOptions(),
                EfCoreStorageProvider.Sqlite);
            modelBuilder.Entity<CustomAuditDocument>(entity =>
            {
                entity.ToTable("custom_audit_documents");
                entity.HasKey(document => document.Id);
                entity.Property(document => document.Id)
                    .HasColumnName("document_id")
                    .ValueGeneratedNever();
                entity.Property(document => document.Marker)
                    .HasColumnName("marker")
                    .IsRequired();
            });
        }
    }
}
