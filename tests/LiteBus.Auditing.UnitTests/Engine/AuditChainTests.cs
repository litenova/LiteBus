using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;

namespace LiteBus.Auditing.UnitTests.Engine;

/// <summary>
///     Verifies that the hash chain facade delegates verification, checkpoints, segments, and erasure to the chain
///     engine components.
/// </summary>
public sealed class AuditChainTests
{
    [Fact]
    public async Task VerifyAsync_WithSegment_ShouldVerifyWholeSegment()
    {
        var verifier = new FakeVerifier();
        var chain = CreateChain(verifier: verifier);

        await chain.VerifyAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);

        verifier.LastRequest.Should().NotBeNull();
        verifier.LastRequest!.Segment.Should().Be("orders/2026");
        verifier.LastRequest.FromSequence.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task VerifyAsync_WithBlankSegment_ShouldThrow(string? segment)
    {
        var chain = CreateChain();

        var act = () => chain.VerifyAsync(segment!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task VerifyAsync_WithRequest_ShouldDelegateVerbatim()
    {
        var verifier = new FakeVerifier();
        var chain = CreateChain(verifier: verifier);
        var request = new AuditChainVerificationRequest("orders/2026", 4, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        var result = await chain.VerifyAsync(request, CancellationToken.None).ConfigureAwait(false);

        verifier.LastRequest.Should().Be(request);
        result.Should().Be(verifier.Result);
    }

    [Fact]
    public async Task VerifyAsync_WithNullRequest_ShouldThrow()
    {
        var chain = CreateChain();

        var act = () => chain.VerifyAsync((AuditChainVerificationRequest) null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task VerifyAsync_WithBlankRequestSegment_ShouldThrow()
    {
        var chain = CreateChain();

        var act = () => chain.VerifyAsync(
            new AuditChainVerificationRequest("  "),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task VerifyFromAsync_WithStoredCheckpoint_ShouldVerifyAfterItsSequence()
    {
        var checkpointId = Guid.NewGuid();
        var checkpointStore = new FakeCheckpointStore();
        checkpointStore.Checkpoints.Add(
            checkpointId,
            Checkpoint(checkpointId, "orders/2026", 9));
        var verifier = new FakeVerifier();
        var chain = CreateChain(verifier: verifier, checkpointStore: checkpointStore);

        await chain.VerifyFromAsync("orders/2026", checkpointId, CancellationToken.None).ConfigureAwait(false);

        verifier.LastRequest.Should().NotBeNull();
        verifier.LastRequest!.Segment.Should().Be("orders/2026");
        verifier.LastRequest.FromSequence.Should().Be(9);
    }

    [Fact]
    public async Task VerifyFromAsync_WithMissingCheckpoint_ShouldReportMissingBreak()
    {
        var checkpointId = Guid.NewGuid();
        var verifier = new FakeVerifier();
        var chain = CreateChain(verifier: verifier, checkpointStore: new FakeCheckpointStore());

        var result = await chain.VerifyFromAsync("orders/2026", checkpointId, CancellationToken.None).ConfigureAwait(false);

        result.IsIntact.Should().BeFalse();
        result.Segment.Should().Be("orders/2026");
        result.RecordCount.Should().Be(0);
        result.Breaks.Should().ContainSingle();
        result.Breaks[0].Kind.Should().Be(AuditChainBreakKind.CheckpointMissing);
        result.Breaks[0].Detail.Should().Contain(checkpointId.ToString());
        verifier.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task VerifyFromAsync_WithCheckpointOfAnotherSegment_ShouldReportMissingBreak()
    {
        var checkpointId = Guid.NewGuid();
        var checkpointStore = new FakeCheckpointStore();
        checkpointStore.Checkpoints.Add(
            checkpointId,
            Checkpoint(checkpointId, "billing/2026", 9));
        var verifier = new FakeVerifier();
        var chain = CreateChain(verifier: verifier, checkpointStore: checkpointStore);

        var result = await chain.VerifyFromAsync("orders/2026", checkpointId, CancellationToken.None).ConfigureAwait(false);

        result.IsIntact.Should().BeFalse();
        result.Breaks.Should().ContainSingle();
        result.Breaks[0].Kind.Should().Be(AuditChainBreakKind.CheckpointMissing);
        result.Breaks[0].Detail.Should().Contain("billing/2026");
        verifier.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task VerifyFromAsync_WithEmptyCheckpointId_ShouldThrow()
    {
        var chain = CreateChain();

        var act = () => chain.VerifyFromAsync("orders/2026", Guid.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task ListSegmentsAsync_ShouldYieldManagerSummaries()
    {
        var segments = new FakeSegmentManager();
        segments.Summaries.Add(new AuditSegmentSummary(
            "orders/2026",
            "tenant-a",
            new Dictionary<string, string>(StringComparer.Ordinal),
            3,
            3,
            null,
            false));
        var chain = CreateChain(segments: segments);

        List<AuditSegmentSummary> listed = [];

        await foreach (var summary in chain
            .ListSegmentsAsync(new AuditSegmentFilter(), CancellationToken.None)
            .ConfigureAwait(false))
        {
            listed.Add(summary);
        }

        listed.Should().ContainSingle();
        listed[0].Key.Should().Be("orders/2026");
        segments.LastFilter.Should().NotBeNull();
    }

    [Fact]
    public void ListSegmentsAsync_WithNullFilter_ShouldThrow()
    {
        var chain = CreateChain();

        var act = () => chain.ListSegmentsAsync(null!, CancellationToken.None);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ListUnpublishedCheckpointsAsync_ShouldYieldStoredCheckpoints()
    {
        var checkpointStore = new FakeCheckpointStore();
        checkpointStore.Unpublished.Add(Checkpoint(Guid.NewGuid(), "orders/2026", 3));
        var chain = CreateChain(checkpointStore: checkpointStore);

        List<AuditCheckpoint> listed = [];

        await foreach (var checkpoint in chain
            .ListUnpublishedCheckpointsAsync(CancellationToken.None)
            .ConfigureAwait(false))
        {
            listed.Add(checkpoint);
        }

        listed.Should().ContainSingle();
    }

    [Fact]
    public async Task EraseAsync_ShouldDelegateFilterAndReason()
    {
        var segments = new FakeSegmentManager { Erased = 2 };
        var chain = CreateChain(segments: segments);
        var filter = new AuditSegmentFilter(TenantId: "tenant-a");

        var erased = await chain.EraseAsync(filter, "retention-expired", CancellationToken.None).ConfigureAwait(false);

        erased.Should().Be(2);
        segments.LastFilter.Should().Be(filter);
        segments.LastReason.Should().Be("retention-expired");
    }

    [Fact]
    public async Task EraseAsync_WithNullFilter_ShouldThrow()
    {
        var chain = CreateChain();

        var act = () => chain.EraseAsync(null!, "retention-expired", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EraseAsync_WithBlankReason_ShouldThrow(string? reason)
    {
        var chain = CreateChain();

        var act = () => chain.EraseAsync(new AuditSegmentFilter(), reason!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task CutCheckpointAsync_ShouldCutRegularCheckpointWithoutLabel()
    {
        var checkpoints = new FakeLifecycle();
        var chain = CreateChain(checkpoints: checkpoints);

        var result = await chain.CutCheckpointAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);

        result.Should().Be(checkpoints.Cut);
        checkpoints.LastSegment.Should().Be("orders/2026");
        checkpoints.LastLabel.Should().BeNull();
        checkpoints.LastKind.Should().Be(AuditCheckpointKind.Regular);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CutCheckpointAsync_WithBlankSegment_ShouldThrow(string? segment)
    {
        var chain = CreateChain();

        var act = () => chain.CutCheckpointAsync(segment!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task ExpireSegmentAsync_ShouldDelegateSegment()
    {
        var segments = new FakeSegmentManager();
        var chain = CreateChain(segments: segments);

        var result = await chain.ExpireSegmentAsync("orders/2026", CancellationToken.None).ConfigureAwait(false);

        result.Should().Be(segments.Expired);
        segments.LastSegment.Should().Be("orders/2026");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ExpireSegmentAsync_WithBlankSegment_ShouldThrow(string? segment)
    {
        var chain = CreateChain();

        var act = () => chain.ExpireSegmentAsync(segment!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public void Constructor_WithNullVerifier_ShouldThrow()
    {
        var act = () => new AuditChain(
            null!,
            new FakeLifecycle(),
            new FakeSegmentManager(),
            new FakeCheckpointStore());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLifecycle_ShouldThrow()
    {
        var act = () => new AuditChain(
            new FakeVerifier(),
            null!,
            new FakeSegmentManager(),
            new FakeCheckpointStore());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullSegments_ShouldThrow()
    {
        var act = () => new AuditChain(
            new FakeVerifier(),
            new FakeLifecycle(),
            null!,
            new FakeCheckpointStore());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullCheckpointStore_ShouldThrow()
    {
        var act = () => new AuditChain(
            new FakeVerifier(),
            new FakeLifecycle(),
            new FakeSegmentManager(),
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static AuditChain CreateChain(
        FakeVerifier? verifier = null,
        FakeLifecycle? checkpoints = null,
        FakeSegmentManager? segments = null,
        FakeCheckpointStore? checkpointStore = null)
    {
        return new AuditChain(
            verifier ?? new FakeVerifier(),
            checkpoints ?? new FakeLifecycle(),
            segments ?? new FakeSegmentManager(),
            checkpointStore ?? new FakeCheckpointStore());
    }

    private static AuditCheckpoint Checkpoint(Guid id, string segment, long sequence)
    {
        return new AuditCheckpoint(
            id,
            segment,
            sequence,
            sequence,
            "hash",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeVerifier : IAuditVerifier
    {
        public AuditChainVerification Result { get; } = new()
        {
            Segment = "orders/2026",
            RecordCount = 1,
            Breaks = [],
        };

        public AuditChainVerificationRequest? LastRequest { get; private set; }

        public ValueTask<AuditChainVerification> VerifyAsync(
            AuditChainVerificationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastRequest = request;

            return ValueTask.FromResult(Result);
        }
    }

    private sealed class FakeLifecycle : ICheckpointLifecycle
    {
        public AuditCheckpoint Cut { get; } = new(
            Guid.NewGuid(),
            "orders/2026",
            3,
            3,
            "hash",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            DateTimeOffset.UtcNow);

        public string? LastSegment { get; private set; }

        public string? LastLabel { get; private set; }

        public AuditCheckpointKind? LastKind { get; private set; }

        public ValueTask<AuditCheckpoint> CutAsync(
            string segment,
            string? label,
            AuditCheckpointKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastSegment = segment;
            LastLabel = label;
            LastKind = kind;

            return ValueTask.FromResult(Cut);
        }

        public ValueTask<int> PublishPendingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0);
        }

        public ValueTask CutDueAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSegmentManager : IAuditSegmentManager
    {
        public List<AuditSegmentSummary> Summaries { get; } = [];

        public AuditCheckpoint Expired { get; } = new(
            Guid.NewGuid(),
            "orders/2026",
            3,
            3,
            "hash",
            "sha256-v2",
            AuditCheckpointKind.Final,
            DateTimeOffset.UtcNow);

        public int Erased { get; set; }

        public AuditSegmentFilter? LastFilter { get; private set; }

        public string? LastReason { get; private set; }

        public string? LastSegment { get; private set; }

        public async IAsyncEnumerable<AuditSegmentSummary> ListAsync(
            AuditSegmentFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastFilter = filter;

            foreach (var summary in Summaries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return summary;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public ValueTask<AuditCheckpoint> ExpireAsync(string segment, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastSegment = segment;

            return ValueTask.FromResult(Expired);
        }

        public ValueTask<int> EraseAsync(
            AuditSegmentFilter filter,
            string reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastFilter = filter;
            LastReason = reason;

            return ValueTask.FromResult(Erased);
        }
    }

    private sealed class FakeCheckpointStore : IAuditCheckpointStore
    {
        public Dictionary<Guid, AuditCheckpoint> Checkpoints { get; } = [];

        public List<AuditCheckpoint> Unpublished { get; } = [];

        public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(
                Checkpoints.TryGetValue(checkpointId, out var checkpoint) ? checkpoint : null);
        }

        public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var checkpoint in Unpublished)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return checkpoint;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
