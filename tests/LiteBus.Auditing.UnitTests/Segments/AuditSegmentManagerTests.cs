using System.Runtime.CompilerServices;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Segments;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Auditing.UnitTests.Segments;

public sealed class AuditSegmentManagerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_WhenHeadsExist_ShouldReturnSummariesMappedFromHeads()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head(
            "workspace:a",
            tenant: "tenant-a",
            attributes: Attr(("class", "tax"), ("year", "2026")),
            lastSequence: 3,
            lastHash: "hash-3",
            count: 3));
        store.SeedHead(Head("workspace:b", lastSequence: 0, count: 0));
        store.SeedHead(Head(
            "workspace:c",
            lastSequence: 10,
            lastHash: "hash-10",
            count: 11,
            lastCheckpointSequence: 8,
            lastCheckpointAt: FrozenNow,
            expired: true));
        var manager = CreateManager(store);
        var filter = new AuditSegmentFilter(IncludeExpired: true);

        List<AuditSegmentSummary> summaries = [];

        await foreach (var summary in manager.ListAsync(filter, CancellationToken.None).ConfigureAwait(false))
        {
            summaries.Add(summary);
        }

        store.LastListFilter.Should().BeSameAs(filter);
        summaries.Should().HaveCount(3);
        var byKey = summaries.ToDictionary(summary => summary.Key);
        var first = byKey["workspace:a"];
        first.TenantId.Should().Be("tenant-a");
        first.Attributes.Should().HaveCount(2);
        first.Attributes["class"].Should().Be("tax");
        first.Attributes["year"].Should().Be("2026");
        first.Count.Should().Be(3);
        first.LastSequence.Should().Be(3);
        first.LastCheckpointSequence.Should().BeNull();
        first.Expired.Should().BeFalse();
        var second = byKey["workspace:b"];
        second.TenantId.Should().BeNull();
        second.LastSequence.Should().BeNull();
        second.Count.Should().Be(0);
        second.Expired.Should().BeFalse();
        var third = byKey["workspace:c"];
        third.LastSequence.Should().Be(10);
        third.Count.Should().Be(11);
        third.LastCheckpointSequence.Should().Be(8);
        third.Expired.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_WhenFilterSelectsTenantAndAttributes_ShouldReturnOnlyMatchingSegments()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", attributes: Attr(("class", "tax")), lastSequence: 1, count: 1));
        store.SeedHead(Head("workspace:b", tenant: "tenant-a", attributes: Attr(("class", "ops")), lastSequence: 1, count: 1));
        store.SeedHead(Head("workspace:c", tenant: "tenant-b", attributes: Attr(("class", "tax")), lastSequence: 1, count: 1));
        var manager = CreateManager(store);
        var filter = new AuditSegmentFilter(TenantId: "tenant-a", Attributes: Attr(("class", "tax")));

        List<AuditSegmentSummary> summaries = [];

        await foreach (var summary in manager.ListAsync(filter, CancellationToken.None).ConfigureAwait(false))
        {
            summaries.Add(summary);
        }

        summaries.Should().ContainSingle().Which.Key.Should().Be("workspace:a");
    }

    [Fact]
    public async Task ListAsync_WhenFilterSelectsOpenedBefore_ShouldExcludeNewerSegments()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:old", lastSequence: 1, count: 1, openedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        store.SeedHead(Head("workspace:new", lastSequence: 1, count: 1, openedAt: FrozenNow));
        var manager = CreateManager(store);
        var filter = new AuditSegmentFilter(OpenedBefore: new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));

        List<AuditSegmentSummary> summaries = [];

        await foreach (var summary in manager.ListAsync(filter, CancellationToken.None).ConfigureAwait(false))
        {
            summaries.Add(summary);
        }

        summaries.Should().ContainSingle().Which.Key.Should().Be("workspace:old");
    }

    [Fact]
    public async Task ListAsync_WhenFilterExcludesExpired_ShouldSkipExpiredSegments()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:open", lastSequence: 1, count: 1));
        store.SeedHead(Head("workspace:expired", lastSequence: 4, count: 4, expired: true));
        var manager = CreateManager(store);

        List<AuditSegmentSummary> summaries = [];

        await foreach (var summary in manager.ListAsync(new AuditSegmentFilter(), CancellationToken.None).ConfigureAwait(false))
        {
            summaries.Add(summary);
        }

        summaries.Should().ContainSingle().Which.Key.Should().Be("workspace:open");
    }

    [Fact]
    public async Task ListAsync_WhenFilterIsNull_ShouldThrow()
    {
        var manager = CreateManager(new FakeAuditChainStore());
        var act = async () =>
        {
            List<AuditSegmentSummary> summaries = [];

            await foreach (var summary in manager.ListAsync(null!, CancellationToken.None).ConfigureAwait(false))
            {
                summaries.Add(summary);
            }
        };

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task ExpireAsync_WhenSegmentHoldsRecords_ShouldCutFinalPublishPendingExpireAndReturnCheckpoint()
    {
        List<string> calls = [];
        var store = new FakeAuditChainStore(calls);
        var lifecycle = new FakeCheckpointLifecycle(calls);
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 5, lastHash: "hash-5", count: 5));
        store.SeedDocument("workspace:a", Doc("workspace:a", 1));
        store.SeedDocument("workspace:a", Doc("workspace:a", 5));
        store.SeedDocument("workspace:a", Doc("workspace:a", 0));
        var kept = new AuditCheckpoint(
            Guid.NewGuid(),
            "workspace:a",
            4,
            4,
            "hash-4",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            FrozenNow);
        store.SeedCheckpoint(kept);
        var final = new AuditCheckpoint(
            Guid.NewGuid(),
            "workspace:a",
            5,
            5,
            "hash-5",
            "sha256-v2",
            AuditCheckpointKind.Final,
            FrozenNow);
        lifecycle.CutHandler = (_, _, _) => final;
        var manager = CreateManager(store, lifecycle);

        var checkpoint = await manager.ExpireAsync("workspace:a", CancellationToken.None).ConfigureAwait(false);

        checkpoint.Should().Be(final);
        (string Segment, string? Label, AuditCheckpointKind Kind) expectedCut = ("workspace:a", null, AuditCheckpointKind.Final);
        lifecycle.CutCalls.Should().ContainSingle().Which.Should().Be(expectedCut);
        lifecycle.PublishPendingCalls.Should().Be(1);
        calls.Should().Equal("cut:workspace:a:Final", "publish-pending", "expire:workspace:a");
        store.TryHead("workspace:a", out var head).Should().BeTrue();
        head!.Expired.Should().BeTrue();
        store.DocumentsFor("workspace:a").Should().BeEmpty();
        store.Checkpoints.Values.Should().ContainSingle().Which.Should().Be(kept);
    }

    [Fact]
    public async Task ExpireAsync_WhenSegmentHasNoHead_ShouldThrowAndCutNothing()
    {
        var store = new FakeAuditChainStore();
        var lifecycle = new FakeCheckpointLifecycle();
        var manager = CreateManager(store, lifecycle);
        var act = async () => await manager.ExpireAsync("workspace:missing", CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*holds no records*");
        lifecycle.CutCalls.Should().BeEmpty();
        lifecycle.PublishPendingCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExpireAsync_WhenSegmentHoldsNoRecords_ShouldThrowAndCutNothing()
    {
        var store = new FakeAuditChainStore();
        var lifecycle = new FakeCheckpointLifecycle();
        store.SeedHead(Head("workspace:a", lastSequence: 0, count: 0));
        var manager = CreateManager(store, lifecycle);
        var act = async () => await manager.ExpireAsync("workspace:a", CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*holds no records*");
        lifecycle.CutCalls.Should().BeEmpty();
        lifecycle.PublishPendingCalls.Should().Be(0);
        store.TryHead("workspace:a", out var head).Should().BeTrue();
        head!.Expired.Should().BeFalse();
    }

    [Fact]
    public async Task ExpireAsync_WhenSegmentExpired_ShouldThrowAndCutNothing()
    {
        var store = new FakeAuditChainStore();
        var lifecycle = new FakeCheckpointLifecycle();
        store.SeedHead(Head("workspace:a", lastSequence: 5, count: 5, expired: true));
        var manager = CreateManager(store, lifecycle);
        var act = async () => await manager.ExpireAsync("workspace:a", CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*has expired*final checkpoint is not stored*");
        lifecycle.CutCalls.Should().BeEmpty();
        lifecycle.PublishPendingCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExpireAsync_WhenSegmentIsBlank_ShouldThrow(string? segment)
    {
        var manager = CreateManager(new FakeAuditChainStore());
        var act = async () => await manager.ExpireAsync(segment!, CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task EraseAsync_WhenNoticesAreOff_ShouldDeleteEverythingMatchingAndPublishNothing()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        store.SeedHead(Head("workspace:b", tenant: "tenant-a", lastSequence: 3, count: 3));
        store.SeedHead(Head("workspace:c", tenant: "tenant-b", lastSequence: 4, count: 4));
        store.SeedDocument("workspace:a", Doc("workspace:a", 1));
        store.SeedDocument("workspace:b", Doc("workspace:b", 1));
        store.SeedDocument("workspace:c", Doc("workspace:c", 1));
        store.SeedCheckpoint(new AuditCheckpoint(
            Guid.NewGuid(),
            "workspace:a",
            2,
            2,
            "hash-2",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            FrozenNow));
        store.SeedCheckpoint(new AuditCheckpoint(
            Guid.NewGuid(),
            "workspace:c",
            4,
            4,
            "hash-4",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            FrozenNow));
        var recorder = new RecordingErasureNoticePublisher();
        using var services = ServicesWith(recorder);
        var manager = CreateManager(store, publishErasureNotices: false, serviceProvider: services);
        var filter = new AuditSegmentFilter(TenantId: "tenant-a");

        var erased = await manager.EraseAsync(filter, "workspace closed", CancellationToken.None).ConfigureAwait(false);

        erased.Should().Be(2);
        store.LastEraseFilter.Should().BeSameAs(filter);
        recorder.Published.Should().BeEmpty();
        store.TryHead("workspace:a", out _).Should().BeFalse();
        store.TryHead("workspace:b", out _).Should().BeFalse();
        store.DocumentsFor("workspace:a").Should().BeEmpty();
        store.DocumentsFor("workspace:b").Should().BeEmpty();
        store.Checkpoints.Values.Should().ContainSingle().Which.Segment.Should().Be("workspace:c");
        store.TryHead("workspace:c", out _).Should().BeTrue();
        store.DocumentsFor("workspace:c").Should().ContainSingle();
    }

    [Fact]
    public async Task EraseAsync_WhenNoticesAreOffAndServiceProviderIsNull_ShouldErase()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        var manager = CreateManager(store, publishErasureNotices: false, serviceProvider: null);

        var erased = await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        erased.Should().Be(1);
        store.TryHead("workspace:a", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EraseAsync_WhenNoticesAreOn_ShouldPublishOneNoticePerErasedSegment()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        store.SeedHead(Head("workspace:b", tenant: "tenant-a", lastSequence: 7, count: 7));
        store.SeedHead(Head("workspace:c", tenant: "tenant-b", lastSequence: 4, count: 4));
        var recorder = new RecordingErasureNoticePublisher();
        using var services = ServicesWith(recorder);
        var manager = CreateManager(store, publishErasureNotices: true, serviceProvider: services);

        var erased = await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        erased.Should().Be(2);
        recorder.Published.Should().HaveCount(2);
        var bySegment = recorder.Published.ToDictionary(notice => notice.Segment);
        bySegment["workspace:a"].Should().Be(new AuditErasureNotice("workspace:a", 2, "workspace closed", FrozenNow));
        bySegment["workspace:b"].Should().Be(new AuditErasureNotice("workspace:b", 7, "workspace closed", FrozenNow));
        store.TryHead("workspace:a", out _).Should().BeFalse();
        store.TryHead("workspace:b", out _).Should().BeFalse();
        store.TryHead("workspace:c", out _).Should().BeTrue();
    }

    [Fact]
    public void AuditErasureNotice_ShouldCarryNoHash()
    {
        typeof(AuditErasureNotice).GetProperty("Hash").Should().BeNull();
        typeof(AuditErasureNotice).GetProperty("HashScheme").Should().BeNull();
        typeof(AuditErasureNotice).GetProperty("PreviousHash").Should().BeNull();
    }

    [Fact]
    public async Task EraseAsync_WhenNoticesAreOnAndNoPublisherIsRegistered_ShouldThrowBeforeDeleting()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        store.SeedDocument("workspace:a", Doc("workspace:a", 1));
        store.SeedCheckpoint(new AuditCheckpoint(
            Guid.NewGuid(),
            "workspace:a",
            2,
            2,
            "hash-2",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            FrozenNow));
        using var services = ServicesWith();
        var manager = CreateManager(store, publishErasureNotices: true, serviceProvider: services);
        var act = async () => await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*no erasure notice publisher*");
        store.TryHead("workspace:a", out _).Should().BeTrue();
        store.DocumentsFor("workspace:a").Should().ContainSingle();
        store.Checkpoints.Should().ContainSingle();
    }

    [Fact]
    public async Task EraseAsync_WhenNoticesAreOnAndServiceProviderIsNull_ShouldThrowBeforeDeleting()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        var manager = CreateManager(store, publishErasureNotices: true, serviceProvider: null);
        var act = async () => await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*no service provider*");
        store.TryHead("workspace:a", out _).Should().BeTrue();
    }

    [Fact]
    public async Task EraseAsync_WhenNothingMatches_ShouldReturnZeroAndPublishNothing()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        var recorder = new RecordingErasureNoticePublisher();
        using var services = ServicesWith(recorder);
        var manager = CreateManager(store, publishErasureNotices: true, serviceProvider: services);

        var erased = await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-absent"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        erased.Should().Be(0);
        recorder.Published.Should().BeEmpty();
        store.TryHead("workspace:a", out _).Should().BeTrue();
    }

    [Fact]
    public async Task EraseAsync_WhenNothingMatchesAndNoPublisherIsRegistered_ShouldReturnZero()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        using var services = ServicesWith();
        var manager = CreateManager(store, publishErasureNotices: true, serviceProvider: services);

        var erased = await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-absent"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        erased.Should().Be(0);
        store.TryHead("workspace:a", out _).Should().BeTrue();
    }

    [Fact]
    public async Task EraseAsync_WhenPublisherThrows_ShouldPropagateAfterDeleting()
    {
        var store = new FakeAuditChainStore();
        store.SeedHead(Head("workspace:a", tenant: "tenant-a", lastSequence: 2, count: 2));
        using var services = ServicesWith(new ThrowingErasureNoticePublisher());
        var manager = CreateManager(store, publishErasureNotices: true, serviceProvider: services);
        var act = async () => await manager
            .EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), "workspace closed", CancellationToken.None)
            .ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.WithMessage("*erasure notice*");
        store.TryHead("workspace:a", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EraseAsync_WhenFilterIsNull_ShouldThrow()
    {
        var manager = CreateManager(new FakeAuditChainStore());
        var act = async () => await manager.EraseAsync(null!, "workspace closed", CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EraseAsync_WhenReasonIsBlank_ShouldThrow(string? reason)
    {
        var manager = CreateManager(new FakeAuditChainStore());
        var act = async () => await manager
            .EraseAsync(new AuditSegmentFilter(), reason!, CancellationToken.None)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    private static AuditSegmentManager<FakeAuditDocument> CreateManager(
        FakeAuditChainStore store,
        FakeCheckpointLifecycle? lifecycle = null,
        bool publishErasureNotices = false,
        IServiceProvider? serviceProvider = null)
    {
        var options = new AuditHashChainOptions { PublishErasureNotices = publishErasureNotices };

        return new AuditSegmentManager<FakeAuditDocument>(
            store,
            lifecycle ?? new FakeCheckpointLifecycle(),
            options,
            new FakeTimeProvider(FrozenNow),
            serviceProvider);
    }

    private static ServiceProvider ServicesWith(params IErasureNoticePublisher[] publishers)
    {
        var services = new ServiceCollection();

        foreach (var publisher in publishers)
        {
            services.AddSingleton(publisher);
        }

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string> Attr(params (string Name, string Value)[] pairs)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (var (name, value) in pairs)
        {
            values[name] = value;
        }

        return values;
    }

    private static AuditSegmentHead Head(
        string key,
        string? tenant = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        DateTimeOffset? openedAt = null,
        long lastSequence = 0,
        string? lastHash = null,
        long count = 0,
        long? lastCheckpointSequence = null,
        DateTimeOffset? lastCheckpointAt = null,
        bool expired = false)
    {
        var info = new AuditSegmentInfo(
            key,
            tenant,
            attributes ?? new Dictionary<string, string>(StringComparer.Ordinal),
            openedAt ?? FrozenNow);

        return new AuditSegmentHead(
            info,
            lastSequence,
            lastHash,
            count,
            lastCheckpointSequence,
            lastCheckpointAt,
            expired,
            1);
    }

    private static FakeAuditDocument Doc(string segment, long sequence)
    {
        return new FakeAuditDocument
        {
            Segment = segment,
            Sequence = sequence,
            PreviousHash = sequence <= 1 ? null : "hash",
            Hash = sequence == 0 ? null : "hash",
            HashScheme = sequence == 0 ? null : "sha256-v2",
        };
    }

    private sealed class FakeAuditDocument : IAuditChainDocument
    {
        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public FakeTimeProvider(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCheckpointLifecycle : ICheckpointLifecycle
    {
        private readonly List<string> _calls;

        public FakeCheckpointLifecycle()
            : this([])
        {
        }

        public FakeCheckpointLifecycle(List<string> calls)
        {
            _calls = calls;
        }

        public List<(string Segment, string? Label, AuditCheckpointKind Kind)> CutCalls { get; } = [];

        public int PublishPendingCalls { get; private set; }

        public int CutDueCalls { get; private set; }

        public Func<string, string?, AuditCheckpointKind, AuditCheckpoint>? CutHandler { get; set; }

        public ValueTask<AuditCheckpoint> CutAsync(
            string segment,
            string? label,
            AuditCheckpointKind kind,
            CancellationToken cancellationToken)
        {
            CutCalls.Add((segment, label, kind));
            _calls.Add($"cut:{segment}:{kind}");
            var checkpoint = CutHandler is null
                ? new AuditCheckpoint(Guid.NewGuid(), segment, 0, 0, "hash", "sha256-v2", kind, FrozenNow)
                : CutHandler(segment, label, kind);

            return ValueTask.FromResult(checkpoint);
        }

        public ValueTask<int> PublishPendingAsync(CancellationToken cancellationToken)
        {
            PublishPendingCalls++;
            _calls.Add("publish-pending");

            return ValueTask.FromResult(0);
        }

        public ValueTask CutDueAsync(CancellationToken cancellationToken)
        {
            CutDueCalls++;

            return default;
        }
    }

    private sealed class FakeAuditChainStore : IAuditChainStore<FakeAuditDocument>
    {
        private readonly Dictionary<string, AuditSegmentHead> _heads = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FakeAuditDocument>> _documents = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, AuditCheckpoint> _checkpoints = new();
        private readonly List<string> _calls;

        public FakeAuditChainStore()
            : this([])
        {
        }

        public FakeAuditChainStore(List<string> calls)
        {
            _calls = calls;
        }

        public AuditSegmentFilter? LastListFilter { get; private set; }

        public AuditSegmentFilter? LastEraseFilter { get; private set; }

        public IReadOnlyDictionary<Guid, AuditCheckpoint> Checkpoints => _checkpoints;

        public void SeedHead(AuditSegmentHead head)
        {
            _heads[head.Info.Key] = head;
        }

        public void SeedDocument(string segment, FakeAuditDocument document)
        {
            if (!_documents.TryGetValue(segment, out var list))
            {
                list = [];
                _documents[segment] = list;
            }

            list.Add(document);
        }

        public void SeedCheckpoint(AuditCheckpoint checkpoint)
        {
            _checkpoints[checkpoint.Id] = checkpoint;
        }

        public bool TryHead(string segment, out AuditSegmentHead? head)
        {
            var found = _heads.TryGetValue(segment, out var current);
            head = current;

            return found;
        }

        public IReadOnlyList<FakeAuditDocument> DocumentsFor(string segment)
        {
            return _documents.TryGetValue(segment, out var list) ? list : [];
        }

        public ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_heads.TryGetValue(segment, out var head) ? head : null);
        }

        public ValueTask AppendAsync(
            string segment,
            AuditSegmentHead expected,
            IReadOnlyList<FakeAuditDocument> documents,
            AuditSegmentHead updated,
            CancellationToken cancellationToken)
        {
            if (!_heads.TryGetValue(segment, out var current))
            {
                if (expected.Version != 0)
                {
                    throw new AuditHeadConflictException($"The audit segment '{segment}' changed between read and write.");
                }

                _heads[segment] = updated with { Version = 1 };
            }
            else
            {
                if (current.Version != expected.Version)
                {
                    throw new AuditHeadConflictException($"The audit segment '{segment}' changed between read and write.");
                }

                _heads[segment] = updated with { Version = current.Version + 1 };
            }

            foreach (var document in documents)
            {
                SeedDocument(segment, document);
            }

            return default;
        }

        public ValueTask StageAsync(string segment, FakeAuditDocument document, CancellationToken cancellationToken)
        {
            document.Segment = segment;
            SeedDocument(segment, document);

            return default;
        }

        public async IAsyncEnumerable<FakeAuditDocument> ListUnlinkedAsync(
            string segment,
            TimeSpan? olderThan,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            if (!_documents.TryGetValue(segment, out var list))
            {
                yield break;
            }

            foreach (var document in list.ToArray())
            {
                if (document.Sequence == 0)
                {
                    yield return document;
                }
            }
        }

        public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
            TimeSpan? olderThan,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var entry in _documents.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.Value.Any(document => document.Sequence == 0))
                {
                    yield return entry.Key;
                }
            }
        }

        public async IAsyncEnumerable<FakeAuditDocument> ReadSegmentAsync(
            string segment,
            long fromSequence,
            long? toSequence,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            if (!_documents.TryGetValue(segment, out var list))
            {
                yield break;
            }

            var upper = toSequence ?? long.MaxValue;

            foreach (var document in list.ToArray())
            {
                if (document.Sequence >= fromSequence && document.Sequence <= upper)
                {
                    yield return document;
                }
            }
        }

        public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
            AuditSegmentFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            LastListFilter = filter;

            foreach (var head in _heads.Values.ToArray())
            {
                if (Matches(head, filter))
                {
                    yield return head;
                }
            }
        }

        public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            _checkpoints[checkpoint.Id] = checkpoint;

            return default;
        }

        public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_checkpoints.TryGetValue(checkpointId, out var checkpoint) ? checkpoint : null);
        }

        public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var checkpoint in _checkpoints.Values.ToArray())
            {
                if (checkpoint.PublishState == CheckpointPublishState.Pending)
                {
                    yield return checkpoint;
                }
            }
        }

        public ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
        {
            if (_checkpoints.TryGetValue(checkpointId, out var checkpoint))
            {
                _checkpoints[checkpointId] = publishError is null
                    ? checkpoint with { PublishState = CheckpointPublishState.Published, PublishAttempts = checkpoint.PublishAttempts + 1 }
                    : checkpoint with { PublishAttempts = checkpoint.PublishAttempts + 1, LastPublishError = publishError };
            }

            return default;
        }

        public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
        {
            LastEraseFilter = filter;
            _calls.Add("erase");
            List<string> erased = [];

            foreach (var key in _heads.Keys.ToArray())
            {
                if (Matches(_heads[key], filter))
                {
                    erased.Add(key);
                }
            }

            foreach (var key in erased)
            {
                _heads.Remove(key);
                _documents.Remove(key);
            }

            foreach (var id in _checkpoints.Keys.ToArray())
            {
                if (erased.Contains(_checkpoints[id].Segment))
                {
                    _checkpoints.Remove(id);
                }
            }

            return default;
        }

        public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
        {
            _calls.Add($"expire:{segment}");

            if (!_heads.TryGetValue(segment, out var head) || head.LastSequence == 0)
            {
                throw new InvalidOperationException($"The audit segment '{segment}' holds no records, so it cannot expire.");
            }

            if (head.Expired)
            {
                throw new InvalidOperationException($"The audit segment '{segment}' has expired already, so it cannot expire again.");
            }

            _documents.Remove(segment);
            _heads[segment] = head with { Expired = true, Version = head.Version + 1 };

            return default;
        }

        private static bool Matches(AuditSegmentHead head, AuditSegmentFilter filter)
        {
            if (filter.TenantId is not null
                && !string.Equals(head.Info.TenantId, filter.TenantId, StringComparison.Ordinal))
            {
                return false;
            }

            if (filter.Attributes is not null)
            {
                foreach (var pair in filter.Attributes)
                {
                    if (!head.Info.Attributes.TryGetValue(pair.Key, out var value)
                        || !string.Equals(value, pair.Value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            if (filter.OpenedBefore is not null && head.Info.OpenedAt >= filter.OpenedBefore)
            {
                return false;
            }

            if (!filter.IncludeExpired && head.Expired)
            {
                return false;
            }

            return true;
        }
    }

    private sealed class RecordingErasureNoticePublisher : IErasureNoticePublisher
    {
        public List<AuditErasureNotice> Published { get; } = [];

        public ValueTask PublishAsync(AuditErasureNotice notice, CancellationToken cancellationToken = default)
        {
            Published.Add(notice);

            return default;
        }
    }

    private sealed class ThrowingErasureNoticePublisher : IErasureNoticePublisher
    {
        public ValueTask PublishAsync(AuditErasureNotice notice, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The erasure notice could not be published.");
        }
    }
}
