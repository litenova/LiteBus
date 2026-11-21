using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.InMemory;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.UnitTests.Capture;

namespace LiteBus.Auditing.UnitTests.Linking;

/// <summary>
///     Verifies that audit records staged with the business change link right after the unit of work commits,
///     and that a link failure leaves the command succeeded with the sweep as the fallback.
/// </summary>
public sealed class AuditLinkCommitListenerTests
{
    /// <summary>
    ///     The segment these tests stage into.
    /// </summary>
    private const string Segment = "orders/2026";

    [Fact]
    public async Task A_command_audited_with_the_default_capture_is_linked_after_its_unit_of_work_commits()
    {
        var store = new InMemoryAuditChainStore<LinkTestDocument>();
        var staged = new AuditStagedSegments();
        var trail = new StagingLinkTestTrail(store, new LinkTestSegmenter(Segment), staged);
        var coordinator = new AuditCaptureCoordinator(trail);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, new AuditingOptions().Capture, CancellationToken.None)
            .ConfigureAwait(false);

        var stagedBeforeCommit = await CollectAsync(
                store.ListUnlinkedAsync(Segment, null, CancellationToken.None),
                CancellationToken.None)
            .ConfigureAwait(false);
        stagedBeforeCommit.Should().ContainSingle();

        var listener = new AuditLinkCommitListener(staged, NewLinker(store));

        await listener.OnCommittedAsync(CancellationToken.None).ConfigureAwait(false);

        var linked = await CollectAsync(
                store.ReadSegmentAsync(Segment, 1, null, CancellationToken.None),
                CancellationToken.None)
            .ConfigureAwait(false);
        linked.Should().ContainSingle();
        linked[0].Sequence.Should().Be(1);
        linked[0].Action.Should().Be(record.Action);

        var unlinked = await CollectAsync(
                store.ListUnlinkedAsync(Segment, null, CancellationToken.None),
                CancellationToken.None)
            .ConfigureAwait(false);
        unlinked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_failure_after_commit_leaves_the_record_for_a_later_sweep()
    {
        var store = new InMemoryAuditChainStore<LinkTestDocument>();
        var staged = new AuditStagedSegments();
        var trail = new StagingLinkTestTrail(store, new LinkTestSegmenter(Segment), staged);
        var coordinator = new AuditCaptureCoordinator(trail);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, new AuditingOptions().Capture, CancellationToken.None)
            .ConfigureAwait(false);

        var failing = new AuditLinkCommitListener(staged, new ThrowingAuditLinker());

        var act = () => failing.OnCommittedAsync(CancellationToken.None);

        await act.Should().NotThrowAsync().ConfigureAwait(false);

        var stillUnlinked = await CollectAsync(
                store.ListUnlinkedAsync(Segment, null, CancellationToken.None),
                CancellationToken.None)
            .ConfigureAwait(false);
        stillUnlinked.Should().ContainSingle();

        var swept = await NewLinker(store).SweepAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);

        swept.Should().Be(1);

        var linked = await CollectAsync(
                store.ReadSegmentAsync(Segment, 1, null, CancellationToken.None),
                CancellationToken.None)
            .ConfigureAwait(false);
        linked.Should().ContainSingle();
        linked[0].Sequence.Should().Be(1);
    }

    /// <summary>
    ///     Builds a linker over the in-memory store for the test segment.
    /// </summary>
    /// <param name="store">The store the linker runs against.</param>
    /// <returns>The linker.</returns>
    private static AuditLinker<LinkTestDocument> NewLinker(InMemoryAuditChainStore<LinkTestDocument> store)
    {
        return new AuditLinker<LinkTestDocument>(
            store,
            new LinkTestSegmenter(Segment),
            new LinkTestSchemeRegistry(LinkTestData.TestScheme()),
            LinkTestData.ChainOptions());
    }

    /// <summary>
    ///     Drains an asynchronous stream into a list.
    /// </summary>
    /// <typeparam name="T">The streamed item type.</typeparam>
    /// <param name="source">The stream to drain.</param>
    /// <param name="cancellationToken">The token used to cancel the enumeration.</param>
    /// <returns>The streamed items in stream order.</returns>
    private static async Task<IReadOnlyList<T>> CollectAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        List<T> items = [];
        var enumerator = source.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                items.Add(enumerator.Current);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        return items;
    }
}

/// <summary>
///     An audit trail that stages every record in the business transaction and records its segment, standing
///     in for a backend trail on its success path.
/// </summary>
internal sealed class StagingLinkTestTrail : IAuditTrail
{
    /// <summary>
    ///     The chain store documents are staged into.
    /// </summary>
    private readonly InMemoryAuditChainStore<LinkTestDocument> _store;

    /// <summary>
    ///     The segmenter that assigns a staged document to its segment.
    /// </summary>
    private readonly IAuditSegmenter<LinkTestDocument> _segmenter;

    /// <summary>
    ///     The holder collecting staged segments for linking after commit.
    /// </summary>
    private readonly AuditStagedSegments _staged;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StagingLinkTestTrail" /> class.
    /// </summary>
    /// <param name="store">The chain store documents are staged into.</param>
    /// <param name="segmenter">The segmenter that assigns a staged document to its segment.</param>
    /// <param name="staged">The holder collecting staged segments for linking after commit.</param>
    public StagingLinkTestTrail(
        InMemoryAuditChainStore<LinkTestDocument> store,
        IAuditSegmenter<LinkTestDocument> segmenter,
        AuditStagedSegments staged)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(staged);

        _store = store;
        _segmenter = segmenter;
        _staged = staged;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var document = new LinkTestDocument { Action = record.Action };
        var segment = _segmenter.SegmentOf(document);

        await _store.StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
        _staged.Add(segment);
    }
}

/// <summary>
///     A linker that fails every link, standing in for a store outage during the commit.
/// </summary>
internal sealed class ThrowingAuditLinker : IAuditLinker
{
    /// <inheritdoc />
    public ValueTask LinkPendingAsync(string segment, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("The audit store is unavailable.");
    }

    /// <inheritdoc />
    public ValueTask<int> SweepAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("The audit store is unavailable.");
    }
}
