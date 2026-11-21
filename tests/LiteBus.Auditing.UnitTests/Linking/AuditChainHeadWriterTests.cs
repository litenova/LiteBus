using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Linking;

namespace LiteBus.Auditing.UnitTests.Linking;

/// <summary>
///     Verifies that the chain head writer numbers, hashes, and appends under head contention.
/// </summary>
public sealed class AuditChainHeadWriterTests
{
    /// <summary>
    ///     The segment every chain in these tests is appended to.
    /// </summary>
    private const string Segment = "orders/2026";

    [Fact]
    public async Task AppendNew_numbers_and_chains_documents_in_order()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);
        var documents = NewDocuments("a", "b", "c");

        var head = await writer.AppendNewAsync(Segment, documents, CancellationToken.None).ConfigureAwait(false);

        var linked = store.Linked(Segment);
        linked.Should().HaveCount(3);
        linked[0].Sequence.Should().Be(1);
        linked[1].Sequence.Should().Be(2);
        linked[2].Sequence.Should().Be(3);
        linked[0].PreviousHash.Should().BeNull();
        linked[1].PreviousHash.Should().Be(linked[0].Hash);
        linked[2].PreviousHash.Should().Be(linked[1].Hash);
        foreach (var document in linked)
        {
            document.Segment.Should().Be(Segment);
            document.Hash.Should().NotBeNullOrWhiteSpace();
            document.HashScheme.Should().Be("sha256-v2");
        }

        head.LastSequence.Should().Be(3);
        head.LastHash.Should().Be(linked[2].Hash);
        head.Count.Should().Be(3);
        head.Version.Should().Be(1);
    }

    [Fact]
    public async Task AppendNew_continues_numbering_on_the_next_append()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);

        await writer.AppendNewAsync(Segment, NewDocuments("a"), CancellationToken.None).ConfigureAwait(false);
        var head = await writer.AppendNewAsync(Segment, NewDocuments("b"), CancellationToken.None).ConfigureAwait(false);

        var linked = store.Linked(Segment);
        linked.Should().HaveCount(2);
        linked[0].Sequence.Should().Be(1);
        linked[1].Sequence.Should().Be(2);
        linked[1].PreviousHash.Should().Be(linked[0].Hash);
        head.LastSequence.Should().Be(2);
        head.Count.Should().Be(2);
        head.Version.Should().Be(2);
    }

    [Fact]
    public async Task AppendNew_opens_a_new_segment_with_a_genesis_document_when_the_segmenter_creates_one()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment, withGenesis: true);

        await writer.AppendNewAsync(Segment, NewDocuments("a"), CancellationToken.None).ConfigureAwait(false);

        var linked = store.Linked(Segment);
        linked.Should().HaveCount(2);
        linked[0].Sequence.Should().Be(1);
        linked[0].PreviousHash.Should().BeNull();
        linked[0].Action.Should().Be("genesis");
        linked[1].Sequence.Should().Be(2);
        linked[1].PreviousHash.Should().Be(linked[0].Hash);
    }

    [Fact]
    public async Task AppendNew_throws_when_the_segment_expired()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);

        await writer.AppendNewAsync(Segment, NewDocuments("a"), CancellationToken.None).ConfigureAwait(false);
        store.MarkExpired(Segment);

        var act = () => writer.AppendNewAsync(Segment, NewDocuments("b"), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task AppendNew_throws_when_no_document_is_given()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);

        var act = () => writer.AppendNewAsync(Segment, [], CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Concurrent_appends_never_share_a_sequence()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);
        List<Task> appends = [];

        for (var index = 0; index < 8; index++)
        {
            appends.Add(writer.AppendNewAsync(Segment, NewDocuments($"a-{index}"), CancellationToken.None).AsTask());
        }

        await Task.WhenAll(appends).ConfigureAwait(false);

        var linked = store.Linked(Segment);
        linked.Should().HaveCount(8);
        var sequences = new HashSet<long>();
        foreach (var document in linked)
        {
            sequences.Add(document.Sequence).Should().BeTrue();
        }

        var head = await store.ReadHeadAsync(Segment, CancellationToken.None).ConfigureAwait(false);
        head!.LastSequence.Should().Be(8);
        head.Count.Should().Be(8);
    }

    [Fact]
    public async Task LinkStaged_links_in_commit_order()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);

        await store.StageAsync(Segment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(Segment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync(Segment, new LinkTestDocument { Action = "c" }, CancellationToken.None).ConfigureAwait(false);

        var linkedCount = await writer.LinkStagedAsync(Segment, null, CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(3);
        store.UnlinkedCount(Segment).Should().Be(0);
        var linked = store.Linked(Segment);
        linked.Should().HaveCount(3);
        linked[0].Action.Should().Be("a");
        linked[1].Action.Should().Be("b");
        linked[2].Action.Should().Be("c");
        linked[0].Sequence.Should().Be(1);
        linked[2].Sequence.Should().Be(3);
    }

    [Fact]
    public async Task LinkStaged_returns_zero_when_nothing_is_staged()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);

        var linkedCount = await writer.LinkStagedAsync(Segment, null, CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(0);
    }

    [Fact]
    public async Task LinkStaged_links_only_records_older_than_the_given_age()
    {
        var start = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LinkTestTimeProvider(start);
        var store = new LinkTestStore(timeProvider);
        var writer = NewWriter(store, Segment);

        await store.StageAsync(Segment, new LinkTestDocument { Action = "old" }, CancellationToken.None).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await store.StageAsync(Segment, new LinkTestDocument { Action = "new" }, CancellationToken.None).ConfigureAwait(false);

        var linkedCount = await writer.LinkStagedAsync(Segment, TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);

        linkedCount.Should().Be(1);
        store.UnlinkedCount(Segment).Should().Be(1);
        var linked = store.Linked(Segment);
        linked.Should().HaveCount(1);
        linked[0].Action.Should().Be("old");
    }

    [Fact]
    public async Task LinkStaged_throws_when_the_segment_expired()
    {
        var store = new LinkTestStore();
        var writer = NewWriter(store, Segment);

        await store.StageAsync(Segment, new LinkTestDocument { Action = "a" }, CancellationToken.None).ConfigureAwait(false);
        await writer.LinkStagedAsync(Segment, null, CancellationToken.None).ConfigureAwait(false);
        store.MarkExpired(Segment);
        await store.StageAsync(Segment, new LinkTestDocument { Action = "b" }, CancellationToken.None).ConfigureAwait(false);

        var act = () => writer.LinkStagedAsync(Segment, null, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds a head writer over a fresh test store for one segment.
    /// </summary>
    /// <param name="store">The store the writer appends to.</param>
    /// <param name="segment">The segment the test segmenter assigns.</param>
    /// <param name="withGenesis">Whether new segments open with a genesis document.</param>
    /// <returns>The writer.</returns>
    private static AuditChainHeadWriter<LinkTestDocument> NewWriter(
        LinkTestStore store,
        string segment,
        bool withGenesis = false)
    {
        return new AuditChainHeadWriter<LinkTestDocument>(
            store,
            new LinkTestSegmenter(segment, withGenesis),
            new LinkTestSchemeRegistry(LinkTestData.TestScheme()),
            LinkTestData.ChainOptions());
    }

    /// <summary>
    ///     Builds new documents with the given actions and no chain positions.
    /// </summary>
    /// <param name="actions">The business actions recorded.</param>
    /// <returns>The new documents.</returns>
    private static List<LinkTestDocument> NewDocuments(params string[] actions)
    {
        List<LinkTestDocument> documents = [];

        foreach (var action in actions)
        {
            documents.Add(new LinkTestDocument { Action = action });
        }

        return documents;
    }
}
