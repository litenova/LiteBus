using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Linking;

namespace LiteBus.Auditing.UnitTests.Linking;

/// <summary>
///     Verifies that the audit linker assigns commit-ordered positions inline and on the sweep.
/// </summary>
public sealed class AuditLinkerTests
{
    /// <summary>
    ///     The first segment these tests stage into.
    /// </summary>
    private const string FirstSegment = "orders/2026";

    /// <summary>
    ///     The second segment these tests stage into.
    /// </summary>
    private const string SecondSegment = "refunds/2026";

    [Fact]
    public async Task LinkPending_links_staged_records_in_commit_order()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);

        await linker.LinkPendingAsync(FirstSegment, CancellationToken.None).ConfigureAwait(false);

        store.UnlinkedCount(FirstSegment).Should().Be(0);
        var linked = store.Linked(FirstSegment);
        linked.Should().HaveCount(2);
        linked[0].Action.Should().Be("a");
        linked[1].Action.Should().Be("b");
        linked[0].Sequence.Should().Be(1);
        linked[1].Sequence.Should().Be(2);
        linked[1].PreviousHash.Should().Be(linked[0].Hash);
    }

    [Fact]
    public async Task LinkPending_is_a_no_op_when_nothing_is_staged()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        await linker.LinkPendingAsync(FirstSegment, CancellationToken.None).ConfigureAwait(false);

        store.Linked(FirstSegment).Should().BeEmpty();
    }

    [Fact]
    public async Task LinkPending_throws_when_the_segment_expired()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await linker.LinkPendingAsync(FirstSegment, CancellationToken.None).ConfigureAwait(false);
        store.MarkExpired(FirstSegment);
        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);

        var act = () => linker.LinkPendingAsync(FirstSegment, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Sweep_links_records_a_crash_left_unlinked_in_commit_order()
    {
        var start = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LinkTestTimeProvider(start);
        var store = new LinkTestStore(timeProvider);
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);

        // The commit succeeded but the inline link never ran; the sweep finds the staged records later.
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var linkedCount = await linker.SweepAsync(TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(2);
        store.UnlinkedCount(FirstSegment).Should().Be(0);
        var linked = store.Linked(FirstSegment);
        linked[0].Action.Should().Be("a");
        linked[1].Action.Should().Be("b");
    }

    [Fact]
    public async Task Sweep_links_a_staged_only_segment_that_has_no_head()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);

        var headBefore = await store.ReadHeadAsync(FirstSegment, CancellationToken.None).ConfigureAwait(false);
        headBefore.Should().BeNull();

        var linkedCount = await linker.SweepAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(1);
        store.UnlinkedCount(FirstSegment).Should().Be(0);
        var linked = store.Linked(FirstSegment);
        linked.Should().ContainSingle();
        linked[0].Action.Should().Be("a");
        linked[0].Sequence.Should().Be(1);

        var headAfter = await store.ReadHeadAsync(FirstSegment, CancellationToken.None).ConfigureAwait(false);
        headAfter.Should().NotBeNull();
        headAfter!.LastSequence.Should().Be(1);
        headAfter.Version.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_links_every_segment_and_reports_the_total()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(SecondSegment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(SecondSegment, new LinkTestDocument { Action = "c" }, CancellationToken.None).ConfigureAwait(false);

        var linkedCount = await linker.SweepAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(3);
        store.Linked(FirstSegment).Should().HaveCount(1);
        store.Linked(SecondSegment).Should().HaveCount(2);
    }

    [Fact]
    public async Task Sweep_skips_records_staged_within_the_given_age()
    {
        var start = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LinkTestTimeProvider(start);
        var store = new LinkTestStore(timeProvider);
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);

        var linkedCount = await linker.SweepAsync(TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(0);
        store.UnlinkedCount(FirstSegment).Should().Be(1);
    }

    [Fact]
    public async Task Sweep_skips_expired_segments()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "old" }, CancellationToken.None).ConfigureAwait(false);
        await linker.LinkPendingAsync(FirstSegment, CancellationToken.None).ConfigureAwait(false);
        store.MarkExpired(FirstSegment);
        await store.StageAsync(FirstSegment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(SecondSegment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);

        var linkedCount = await linker.SweepAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(1);
        store.Linked(SecondSegment).Should().HaveCount(1);
        store.UnlinkedCount(FirstSegment).Should().Be(1);
    }

    [Fact]
    public async Task Sweep_throws_for_a_negative_age()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);

        var act = () => linker.SweepAsync(TimeSpan.FromMinutes(-1), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Concurrent_linkers_never_duplicate_a_sequence()
    {
        var store = new LinkTestStore();
        var first = NewLinker(store, FirstSegment);
        var second = NewLinker(store, FirstSegment);

        for (var index = 0; index < 10; index++)
        {
            await store.StageAsync(FirstSegment, new LinkTestDocument { Action = $"a-{index}" }, CancellationToken.None).ConfigureAwait(false);
        }

        await Task.WhenAll(
            first.LinkPendingAsync(FirstSegment, CancellationToken.None).AsTask(),
            second.LinkPendingAsync(FirstSegment, CancellationToken.None).AsTask()).ConfigureAwait(false);

        var linked = store.Linked(FirstSegment);
        linked.Should().HaveCount(10);
        store.UnlinkedCount(FirstSegment).Should().Be(0);

        var sequences = new HashSet<long>();
        foreach (var document in linked)
        {
            sequences.Add(document.Sequence).Should().BeTrue();
        }

        for (var sequence = 1; sequence <= 10; sequence++)
        {
            sequences.Should().Contain((long) sequence);
        }
    }

    [Fact]
    public async Task A_link_racing_a_sweep_never_duplicates_a_sequence()
    {
        var store = new LinkTestStore();
        var linker = NewLinker(store, FirstSegment);
        var sweeper = NewLinker(store, FirstSegment);

        for (var index = 0; index < 6; index++)
        {
            await store.StageAsync(FirstSegment, new LinkTestDocument { Action = $"a-{index}" }, CancellationToken.None).ConfigureAwait(false);
        }

        await Task.WhenAll(
            linker.LinkPendingAsync(FirstSegment, CancellationToken.None).AsTask(),
            sweeper.SweepAsync(TimeSpan.Zero, CancellationToken.None).AsTask()).ConfigureAwait(false);

        var linked = store.Linked(FirstSegment);
        linked.Should().HaveCount(6);
        store.UnlinkedCount(FirstSegment).Should().Be(0);
    }

    /// <summary>
    ///     Builds a linker over a fresh test store for one segment.
    /// </summary>
    /// <param name="store">The store the linker runs against.</param>
    /// <param name="segment">The segment the test segmenter assigns.</param>
    /// <returns>The linker.</returns>
    private static AuditLinker<LinkTestDocument> NewLinker(LinkTestStore store, string segment)
    {
        return new AuditLinker<LinkTestDocument>(
            store,
            new LinkTestSegmenter(segment),
            new LinkTestSchemeRegistry(LinkTestData.TestScheme()),
            LinkTestData.ChainOptions());
    }
}
