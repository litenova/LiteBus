using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.InMemory;
using LiteBus.Testing;

namespace LiteBus.Auditing.UnitTests.Engine;

/// <summary>
///     Verifies that the in-memory audit chain store appends, stages, reads, checkpoints, and erases segments.
/// </summary>
public sealed class InMemoryAuditChainStoreTests
{
    [Fact]
    public async Task AppendAsync_FirstAppend_ShouldCreateHeadVerbatim()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var info = SegmentInfo("orders/2026");
        var expected = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var first = new StoreTestDocument { Segment = "orders/2026", Sequence = 1, Hash = "h1" };
        var second = new StoreTestDocument { Segment = "orders/2026", Sequence = 2, PreviousHash = "h1", Hash = "h2" };
        var updated = new AuditSegmentHead(info, 2, "h2", 2, null, null, false, 1);

        await store.AppendAsync("orders/2026", expected, [first, second], updated, CancellationToken.None)
            .ConfigureAwait(false);

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        head.Should().Be(updated);

        var documents = await DrainAsync(store.ReadSegmentAsync("orders/2026", 1, null, CancellationToken.None))
            .ConfigureAwait(false);
        documents.Select(document => document.Sequence).Should().Equal(1L, 2L);
    }

    [Fact]
    public async Task AppendAsync_WithStaleExpectedVersion_ShouldThrowConflict()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 1).ConfigureAwait(false);

        var info = SegmentInfo("orders/2026");
        var stale = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var updated = new AuditSegmentHead(info, 1, "h1", 1, null, null, false, 1);

        Func<Task> act = async () => await store.AppendAsync(
            "orders/2026",
            stale,
            [new StoreTestDocument { Segment = "orders/2026", Sequence = 1, Hash = "h1" }],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false)).WithMessage("*expected head version 0 but found 1*");
    }

    [Fact]
    public async Task AppendAsync_WithoutHeadAndNonZeroExpected_ShouldThrowConflict()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var info = SegmentInfo("orders/2026");
        var expected = new AuditSegmentHead(info, 4, "h4", 4, null, null, false, 4);
        var updated = new AuditSegmentHead(info, 5, "h5", 5, null, null, false, 5);

        Func<Task> act = async () => await store.AppendAsync(
            "orders/2026",
            expected,
            [new StoreTestDocument { Segment = "orders/2026", Sequence = 5, Hash = "h5" }],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false)).WithMessage("*no stored head*");
    }

    [Fact]
    public async Task AppendAsync_WithEmptyDocuments_ShouldMoveHeadOnly()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 2).ConfigureAwait(false);

        var before = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        var updated = before! with { LastCheckpointSequence = 2, Version = before.Version + 1 };

        await store.AppendAsync("orders/2026", before, [], updated, CancellationToken.None).ConfigureAwait(false);

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        head.Should().Be(updated);

        var documents = await DrainAsync(store.ReadSegmentAsync("orders/2026", 1, null, CancellationToken.None))
            .ConfigureAwait(false);
        documents.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppendAsync_WithUpdatedHeadOfAnotherSegment_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var expected = new AuditSegmentHead(SegmentInfo("a"), 0, null, 0, null, null, false, 0);
        var updated = new AuditSegmentHead(SegmentInfo("b"), 1, "h1", 1, null, null, false, 1);

        Func<Task> act = async () => await store.AppendAsync(
            "a",
            expected,
            [new StoreTestDocument { Segment = "a", Sequence = 1, Hash = "h1" }],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*targets segment 'b'*");
    }

    [Fact]
    public async Task AppendAsync_WithDocumentOfAnotherSegment_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var info = SegmentInfo("a");
        var expected = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var updated = new AuditSegmentHead(info, 1, "h1", 1, null, null, false, 1);

        Func<Task> act = async () => await store.AppendAsync(
            "a",
            expected,
            [new StoreTestDocument { Segment = "b", Sequence = 1, Hash = "h1" }],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*carries segment 'b'*");
    }

    [Fact]
    public async Task AppendAsync_WithNonIncreasingSequences_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var info = SegmentInfo("orders/2026");
        var expected = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var updated = new AuditSegmentHead(info, 2, "h2", 2, null, null, false, 1);

        Func<Task> act = async () => await store.AppendAsync(
            "orders/2026",
            expected,
            [
                new StoreTestDocument { Segment = "orders/2026", Sequence = 2, Hash = "h2" },
                new StoreTestDocument { Segment = "orders/2026", Sequence = 2, Hash = "h2b" },
            ],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*strictly increasing*");
    }

    [Fact]
    public async Task AppendAsync_WithDuplicateLinkedSequence_ShouldThrowConflict()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 1).ConfigureAwait(false);

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        var updated = head! with { Version = head.Version + 1 };

        Func<Task> act = async () => await store.AppendAsync(
            "orders/2026",
            head,
            [new StoreTestDocument { Segment = "orders/2026", Sequence = 1, Hash = "h1b" }],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false)).WithMessage("*already contains sequence 1*");
    }

    [Fact]
    public async Task AppendAsync_WithExpiredHead_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 1).ConfigureAwait(false);
        await MarkExpiredAsync(store, "orders/2026").ConfigureAwait(false);

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        var updated = head! with { Version = head.Version + 1 };

        Func<Task> act = async () => await store.AppendAsync(
            "orders/2026",
            head,
            [new StoreTestDocument { Segment = "orders/2026", Sequence = 2, Hash = "h2" }],
            updated,
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*has expired*");
    }

    [Fact]
    public async Task AppendAsync_ShouldMoveStagedDocumentsToLinked()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var first = new StoreTestDocument { Note = "first" };
        var second = new StoreTestDocument { Note = "second" };

        await store.StageAsync("orders/2026", first, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync("orders/2026", second, CancellationToken.None).ConfigureAwait(false);

        first.Sequence = 1;
        first.Hash = "h1";
        second.Sequence = 2;
        second.PreviousHash = "h1";
        second.Hash = "h2";

        var info = SegmentInfo("orders/2026");
        var expected = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var updated = new AuditSegmentHead(info, 2, "h2", 2, null, null, false, 1);

        await store.AppendAsync("orders/2026", expected, [first, second], updated, CancellationToken.None)
            .ConfigureAwait(false);

        var unlinked = await DrainAsync(store.ListUnlinkedAsync("orders/2026", null, CancellationToken.None))
            .ConfigureAwait(false);
        unlinked.Should().BeEmpty();

        var linked = await DrainAsync(store.ReadSegmentAsync("orders/2026", 1, null, CancellationToken.None))
            .ConfigureAwait(false);
        linked.Select(document => document.Note).Should().Equal("first", "second");
    }

    [Fact]
    public async Task StageAsync_ShouldAssignSegmentAndOpenPlaceholderHead()
    {
        var start = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryAuditChainStore<StoreTestDocument>(new ManualTimeProvider(start));
        var document = new StoreTestDocument();

        await store.StageAsync("orders/2026", document, CancellationToken.None).ConfigureAwait(false);

        document.Segment.Should().Be("orders/2026");

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.Version.Should().Be(0);
        head.Count.Should().Be(0);
        head.LastSequence.Should().Be(0);
        head.Info.Key.Should().Be("orders/2026");
        head.Info.OpenedAt.Should().Be(start);
    }

    [Fact]
    public async Task StageAsync_WithSequencedDocument_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        Func<Task> act = async () => await store.StageAsync(
            "orders/2026",
            new StoreTestDocument { Sequence = 3 },
            CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*already carries sequence 3*");
    }

    [Fact]
    public async Task StageAsync_SameInstanceTwice_ShouldReplaceEntry()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryAuditChainStore<StoreTestDocument>(clock);
        var document = new StoreTestDocument();

        await store.StageAsync("orders/2026", document, CancellationToken.None).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromMinutes(10));

        await store.StageAsync("orders/2026", document, CancellationToken.None).ConfigureAwait(false);

        var unlinked = await DrainAsync(store.ListUnlinkedAsync("orders/2026", null, CancellationToken.None))
            .ConfigureAwait(false);
        unlinked.Should().ContainSingle();

        var aged = await DrainAsync(
                store.ListUnlinkedAsync("orders/2026", TimeSpan.FromMinutes(5), CancellationToken.None))
            .ConfigureAwait(false);
        aged.Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_OnExpiredHead_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 1).ConfigureAwait(false);
        await MarkExpiredAsync(store, "orders/2026").ConfigureAwait(false);

        Func<Task> act = async () => await store.StageAsync("orders/2026", new StoreTestDocument(), CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*has expired*");
    }

    [Fact]
    public async Task StageAsync_SecondStage_ShouldKeepFirstHead()
    {
        var start = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        var store = new InMemoryAuditChainStore<StoreTestDocument>(clock);

        await store.StageAsync("orders/2026", new StoreTestDocument(), CancellationToken.None).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromHours(1));

        await store.StageAsync("orders/2026", new StoreTestDocument(), CancellationToken.None).ConfigureAwait(false);

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        head!.Info.OpenedAt.Should().Be(start);
    }

    [Fact]
    public async Task ListUnlinkedAsync_ShouldReturnCommitOrder()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        await store.StageAsync("orders/2026", new StoreTestDocument { Note = "first" }, CancellationToken.None)
            .ConfigureAwait(false);
        await store.StageAsync("orders/2026", new StoreTestDocument { Note = "second" }, CancellationToken.None)
            .ConfigureAwait(false);
        await store.StageAsync("orders/2026", new StoreTestDocument { Note = "third" }, CancellationToken.None)
            .ConfigureAwait(false);

        var unlinked = await DrainAsync(store.ListUnlinkedAsync("orders/2026", null, CancellationToken.None))
            .ConfigureAwait(false);

        unlinked.Select(document => document.Note).Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task ListUnlinkedAsync_WithOlderThan_ShouldFilterInclusively()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryAuditChainStore<StoreTestDocument>(clock);

        await store.StageAsync("orders/2026", new StoreTestDocument { Note = "old" }, CancellationToken.None)
            .ConfigureAwait(false);

        clock.Advance(TimeSpan.FromMinutes(5));

        await store.StageAsync("orders/2026", new StoreTestDocument { Note = "new" }, CancellationToken.None)
            .ConfigureAwait(false);

        var aged = await DrainAsync(
                store.ListUnlinkedAsync("orders/2026", TimeSpan.FromMinutes(5), CancellationToken.None))
            .ConfigureAwait(false);

        aged.Select(document => document.Note).Should().Equal("old");
    }

    [Fact]
    public async Task ListUnlinkedAsync_WithNegativeOlderThan_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        var act = () => DrainAsync(
            store.ListUnlinkedAsync("orders/2026", TimeSpan.FromMinutes(-1), CancellationToken.None));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task ReadSegmentAsync_ShouldPageByRange()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 5).ConfigureAwait(false);

        var page = await DrainAsync(store.ReadSegmentAsync("orders/2026", 2, 4, CancellationToken.None))
            .ConfigureAwait(false);
        page.Select(document => document.Sequence).Should().Equal(2L, 3L, 4L);

        var tail = await DrainAsync(store.ReadSegmentAsync("orders/2026", 4, null, CancellationToken.None))
            .ConfigureAwait(false);
        tail.Select(document => document.Sequence).Should().Equal(4L, 5L);

        var all = await DrainAsync(store.ReadSegmentAsync("orders/2026", 0, null, CancellationToken.None))
            .ConfigureAwait(false);
        all.Should().HaveCount(5);
    }

    [Fact]
    public async Task ReadSegmentAsync_WithNegativeFrom_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        var act = () => DrainAsync(store.ReadSegmentAsync("orders/2026", -1, null, CancellationToken.None));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task ReadSegmentAsync_WithToBeforeFrom_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        var act = () => DrainAsync(store.ReadSegmentAsync("orders/2026", 4, 2, CancellationToken.None));

        (await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false)).WithMessage("*must not precede*");
    }

    [Fact]
    public async Task ReadSegmentAsync_ShouldExcludeStagedDocuments()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var staged = new StoreTestDocument();

        await store.StageAsync("orders/2026", staged, CancellationToken.None).ConfigureAwait(false);

        staged.Sequence = 7;
        staged.Hash = "h7";

        var linked = await DrainAsync(store.ReadSegmentAsync("orders/2026", 1, null, CancellationToken.None))
            .ConfigureAwait(false);
        linked.Should().BeEmpty();

        var unlinked = await DrainAsync(store.ListUnlinkedAsync("orders/2026", null, CancellationToken.None))
            .ConfigureAwait(false);
        unlinked.Should().ContainSingle();
    }

    [Fact]
    public async Task ListHeadsAsync_ShouldMatchPredicatesInKeyOrder()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var early = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await OpenHeadAsync(store, "b-billing", "tenant-b", new Dictionary<string, string>(StringComparer.Ordinal), late, expired: false).ConfigureAwait(false);
        await OpenHeadAsync(
                store,
                "a-orders",
                "tenant-a",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "orders" },
                early,
                expired: false)
            .ConfigureAwait(false);
        await OpenHeadAsync(store, "c-legacy", "tenant-a", new Dictionary<string, string>(StringComparer.Ordinal), early, expired: true).ConfigureAwait(false);

        var all = await DrainAsync(store.ListHeadsAsync(new AuditSegmentFilter(), CancellationToken.None))
            .ConfigureAwait(false);
        all.Select(head => head.Info.Key).Should().Equal("a-orders", "b-billing");

        var tenant = await DrainAsync(
                store.ListHeadsAsync(new AuditSegmentFilter(TenantId: "tenant-a"), CancellationToken.None))
            .ConfigureAwait(false);
        tenant.Select(head => head.Info.Key).Should().Equal("a-orders");

        var attributed = await DrainAsync(
                store.ListHeadsAsync(
                    new AuditSegmentFilter(
                        Attributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "orders" }),
                    CancellationToken.None))
            .ConfigureAwait(false);
        attributed.Select(head => head.Info.Key).Should().Equal("a-orders");

        var aged = await DrainAsync(
                store.ListHeadsAsync(new AuditSegmentFilter(OpenedBefore: late), CancellationToken.None))
            .ConfigureAwait(false);
        aged.Select(head => head.Info.Key).Should().Equal("a-orders");

        var withExpired = await DrainAsync(
                store.ListHeadsAsync(new AuditSegmentFilter(IncludeExpired: true), CancellationToken.None))
            .ConfigureAwait(false);
        withExpired.Select(head => head.Info.Key).Should().Equal("a-orders", "b-billing", "c-legacy");
    }

    [Fact]
    public async Task ListHeadsAsync_WithNullFilter_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        var act = () => DrainAsync(store.ListHeadsAsync(null!, CancellationToken.None));

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task StoreCheckpointAsync_ShouldUpsertWithoutTouchingHead()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 2).ConfigureAwait(false);

        var checkpointId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

        await store.StoreCheckpointAsync(Checkpoint(checkpointId, "orders/2026", 2, createdAt), CancellationToken.None)
            .ConfigureAwait(false);
        await store.StoreCheckpointAsync(
                Checkpoint(checkpointId, "orders/2026", 2, createdAt) with
                {
                    PublishState = CheckpointPublishState.Published,
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        var stored = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.PublishState.Should().Be(CheckpointPublishState.Published);

        var head = await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);
        head!.LastCheckpointSequence.Should().BeNull();
        head.LastCheckpointAt.Should().BeNull();
    }

    [Fact]
    public async Task ReadCheckpointAsync_Missing_ShouldReturnNull()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        var stored = await store.ReadCheckpointAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false);

        stored.Should().BeNull();
    }

    [Fact]
    public async Task ReadCheckpointAsync_WithEmptyId_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        Func<Task> act = async () =>
        {
            await store.ReadCheckpointAsync(Guid.Empty, CancellationToken.None).ConfigureAwait(false);
        };

        (await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false)).WithMessage("*must not be empty*");
    }

    [Fact]
    public async Task ListUnpublishedAsync_ShouldReturnPendingOldestFirst()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var old = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var recent = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

        await store.StoreCheckpointAsync(Checkpoint(Guid.NewGuid(), "orders/2026", 4, recent), CancellationToken.None)
            .ConfigureAwait(false);
        await store.StoreCheckpointAsync(Checkpoint(Guid.NewGuid(), "orders/2026", 2, old), CancellationToken.None)
            .ConfigureAwait(false);

        var publishedId = Guid.NewGuid();

        await store.StoreCheckpointAsync(Checkpoint(publishedId, "orders/2026", 6, recent), CancellationToken.None)
            .ConfigureAwait(false);
        await store.MarkPublishedAsync(publishedId, null, CancellationToken.None).ConfigureAwait(false);

        var unpublished = await DrainAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);

        unpublished.Select(checkpoint => checkpoint.Sequence).Should().Equal(2L, 4L);
    }

    [Fact]
    public async Task MarkPublishedAsync_ShouldTrackAttemptsAndErrors()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var checkpointId = Guid.NewGuid();

        await store.StoreCheckpointAsync(Checkpoint(checkpointId, "orders/2026", 2), CancellationToken.None)
            .ConfigureAwait(false);
        await store.MarkPublishedAsync(checkpointId, "connection refused", CancellationToken.None)
            .ConfigureAwait(false);

        var pending = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
        pending!.PublishState.Should().Be(CheckpointPublishState.Pending);
        pending.PublishAttempts.Should().Be(1);
        pending.LastPublishError.Should().Be("connection refused");

        await store.MarkPublishedAsync(checkpointId, null, CancellationToken.None).ConfigureAwait(false);

        var published = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
        published!.PublishState.Should().Be(CheckpointPublishState.Published);
        published.PublishAttempts.Should().Be(2);
        published.LastPublishError.Should().BeNull();
    }

    [Fact]
    public async Task MarkPublishedAsync_Missing_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        Func<Task> act = async () => await store.MarkPublishedAsync(Guid.NewGuid(), null, CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).WithMessage("*No stored audit checkpoint carries*");
    }

    [Fact]
    public async Task MarkPublishedAsync_WithEmptyId_ShouldThrow()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();

        Func<Task> act = async () => await store.MarkPublishedAsync(Guid.Empty, null, CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false)).WithMessage("*must not be empty*");
    }

    [Fact]
    public async Task EraseAsync_ShouldDeleteMatchedDocumentsHeadsAndCheckpoints()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await OpenHeadAsync(store, "tenant-a/orders", "tenant-a", new Dictionary<string, string>(StringComparer.Ordinal), null, expired: false).ConfigureAwait(false);
        await OpenHeadAsync(store, "tenant-b/orders", "tenant-b", new Dictionary<string, string>(StringComparer.Ordinal), null, expired: false).ConfigureAwait(false);
        await AppendLinkedAsync(store, "tenant-a/orders", 2).ConfigureAwait(false);
        await AppendLinkedAsync(store, "tenant-b/orders", 2).ConfigureAwait(false);
        await store.StageAsync("tenant-a/orders", new StoreTestDocument(), CancellationToken.None).ConfigureAwait(false);

        var erasedCheckpointId = Guid.NewGuid();
        var keptCheckpointId = Guid.NewGuid();

        await store.StoreCheckpointAsync(Checkpoint(erasedCheckpointId, "tenant-a/orders", 2), CancellationToken.None)
            .ConfigureAwait(false);
        await store.StoreCheckpointAsync(Checkpoint(keptCheckpointId, "tenant-b/orders", 2), CancellationToken.None)
            .ConfigureAwait(false);

        await store.EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), CancellationToken.None)
            .ConfigureAwait(false);

        (await store.ReadHeadAsync("tenant-a/orders", CancellationToken.None).ConfigureAwait(false))
            .Should().BeNull();
        (await DrainAsync(store.ReadSegmentAsync("tenant-a/orders", 1, null, CancellationToken.None))
            .ConfigureAwait(false)).Should().BeEmpty();
        (await DrainAsync(store.ListUnlinkedAsync("tenant-a/orders", null, CancellationToken.None))
            .ConfigureAwait(false)).Should().BeEmpty();
        (await store.ReadCheckpointAsync(erasedCheckpointId, CancellationToken.None).ConfigureAwait(false))
            .Should().BeNull();

        (await store.ReadHeadAsync("tenant-b/orders", CancellationToken.None).ConfigureAwait(false))
            .Should().NotBeNull();
        (await store.ReadCheckpointAsync(keptCheckpointId, CancellationToken.None).ConfigureAwait(false))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task EraseAsync_ShouldIgnoreExpiry()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "orders/2026", 1).ConfigureAwait(false);
        await MarkExpiredAsync(store, "orders/2026").ConfigureAwait(false);

        await store.EraseAsync(new AuditSegmentFilter(), CancellationToken.None).ConfigureAwait(false);

        (await store.ReadHeadAsync("orders/2026", CancellationToken.None).ConfigureAwait(false)).Should().BeNull();
    }

    [Fact]
    public async Task EraseAsync_WithEmptyFilter_ShouldEraseAll()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        await AppendLinkedAsync(store, "a", 1).ConfigureAwait(false);
        await AppendLinkedAsync(store, "b", 1).ConfigureAwait(false);

        await store.EraseAsync(new AuditSegmentFilter(), CancellationToken.None).ConfigureAwait(false);

        var heads = await DrainAsync(store.ListHeadsAsync(new AuditSegmentFilter(IncludeExpired: true), CancellationToken.None))
            .ConfigureAwait(false);
        heads.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_ShouldReturnHighestSequenceThenNewest()
    {
        var store = new InMemoryAuditChainStore<StoreTestDocument>();
        var old = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var recent = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

        await store.StoreCheckpointAsync(Checkpoint(Guid.NewGuid(), "orders/2026", 3, old), CancellationToken.None)
            .ConfigureAwait(false);
        await store.StoreCheckpointAsync(Checkpoint(Guid.NewGuid(), "orders/2026", 5, old), CancellationToken.None)
            .ConfigureAwait(false);

        var newestId = Guid.NewGuid();

        await store.StoreCheckpointAsync(Checkpoint(newestId, "orders/2026", 5, recent), CancellationToken.None)
            .ConfigureAwait(false);
        await store.StoreCheckpointAsync(Checkpoint(Guid.NewGuid(), "billing/2026", 9, recent), CancellationToken.None)
            .ConfigureAwait(false);

        var latest = await store.GetLatestAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(newestId);

        var missing = await store.GetLatestAsync("unknown", CancellationToken.None).ConfigureAwait(false);
        missing.Should().BeNull();
    }

    private static async Task AppendLinkedAsync(
        InMemoryAuditChainStore<StoreTestDocument> store,
        string segment,
        int count)
    {
        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        var expected = head ?? new AuditSegmentHead(SegmentInfo(segment), 0, null, 0, null, null, false, 0);

        List<StoreTestDocument> documents = [];
        var sequence = expected.LastSequence;
        string? previous = expected.LastHash;

        for (var index = 0; index < count; index++)
        {
            sequence++;

            documents.Add(new StoreTestDocument
            {
                Segment = segment,
                Sequence = sequence,
                PreviousHash = previous,
                Hash = "h" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                HashScheme = "sha256-v2",
            });

            previous = "h" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var updated = expected with
        {
            LastSequence = sequence,
            LastHash = previous,
            Count = expected.Count + documents.Count,
            Version = expected.Version + 1,
        };

        await store.AppendAsync(segment, expected, documents, updated, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task OpenHeadAsync(
        InMemoryAuditChainStore<StoreTestDocument> store,
        string segment,
        string? tenantId,
        IReadOnlyDictionary<string, string> attributes,
        DateTimeOffset? openedAt,
        bool expired)
    {
        var info = new AuditSegmentInfo(
            segment,
            tenantId,
            attributes,
            openedAt ?? new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        var expected = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var updated = new AuditSegmentHead(info, 0, null, 0, null, null, expired, 1);

        await store.AppendAsync(segment, expected, [], updated, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task MarkExpiredAsync(InMemoryAuditChainStore<StoreTestDocument> store, string segment)
    {
        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        var updated = head! with { Expired = true, Version = head.Version + 1 };

        await store.AppendAsync(segment, head, [], updated, CancellationToken.None).ConfigureAwait(false);
    }

    private static AuditSegmentInfo SegmentInfo(string segment)
    {
        return new AuditSegmentInfo(
            segment,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
    }

    private static AuditCheckpoint Checkpoint(Guid id, string segment, long sequence)
    {
        return Checkpoint(id, segment, sequence, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
    }

    private static AuditCheckpoint Checkpoint(Guid id, string segment, long sequence, DateTimeOffset createdAt)
    {
        return new AuditCheckpoint(
            id,
            segment,
            sequence,
            sequence,
            "hash",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            createdAt);
    }

    private static async Task<List<T>> DrainAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed class StoreTestDocument : IAuditChainDocument
    {
        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }

        public string? Note { get; set; }
    }
}
