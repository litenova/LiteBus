using System.Runtime.CompilerServices;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing;
using LiteBus.Auditing.Checkpoints;

namespace LiteBus.Auditing.UnitTests.Checkpoints;

/// <summary>
///     Verifies that checkpoint cuts store pending signed checkpoints, publish passes publish and record failures, and
///     due scans cut where the policy reports due.
/// </summary>
public sealed class CheckpointLifecycleTests
{
    /// <summary>
    ///     The segment every head in these tests belongs to unless another is named.
    /// </summary>
    private const string Segment = "orders/2026";

    /// <summary>
    ///     Builds chain options with the given policy and no container pieces.
    /// </summary>
    /// <param name="policy">The checkpoint policy.</param>
    /// <returns>The options.</returns>
    private static AuditHashChainOptions ChainOptions(ICheckpointPolicy policy)
    {
        return new AuditHashChainOptions { CheckpointPolicy = policy };
    }

    /// <summary>
    ///     Builds chain options with the given policy and signer types.
    /// </summary>
    /// <param name="policy">The checkpoint policy.</param>
    /// <param name="signerTypes">The signer types.</param>
    /// <returns>The options.</returns>
    private static AuditHashChainOptions ChainOptionsWithSigners(ICheckpointPolicy policy, params Type[] signerTypes)
    {
        return new AuditHashChainOptions { CheckpointPolicy = policy, SignerTypes = [.. signerTypes] };
    }

    /// <summary>
    ///     Builds chain options with the given policy and publisher types.
    /// </summary>
    /// <param name="policy">The checkpoint policy.</param>
    /// <param name="publisherTypes">The publisher types.</param>
    /// <returns>The options.</returns>
    private static AuditHashChainOptions ChainOptionsWithPublishers(ICheckpointPolicy policy, params Type[] publisherTypes)
    {
        return new AuditHashChainOptions { CheckpointPolicy = policy, PublisherTypes = [.. publisherTypes] };
    }

    /// <summary>
    ///     Collects an asynchronous sequence into a list.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence.</param>
    /// <returns>The elements.</returns>
    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public async Task CutAsync_stores_a_pending_signed_checkpoint_and_advances_the_head()
    {
        var store = new InMemoryAuditChainStore();
        var head = store.SeedSegment(Segment, count: 5, hash: "aabb");
        var signer = new FixedSigner("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=");
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptionsWithSigners(CheckpointPolicies.Never(), typeof(FixedSigner)),
            new FixedServiceProvider([(typeof(FixedSigner), signer)]),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero)));

        var checkpoint = await lifecycle
            .CutAsync(Segment, "daily", AuditCheckpointKind.Regular, CancellationToken.None)
            .ConfigureAwait(false);

        checkpoint.Segment.Should().Be(Segment);
        checkpoint.Sequence.Should().Be(head.LastSequence);
        checkpoint.Count.Should().Be(head.Count);
        checkpoint.Hash.Should().Be("aabb");
        checkpoint.Label.Should().Be("daily");
        checkpoint.PublishState.Should().Be(CheckpointPublishState.Pending);
        checkpoint.PublishAttempts.Should().Be(0);
        checkpoint.Signatures.Should().HaveCount(1);
        checkpoint.Signature.Should().Be(checkpoint.Signatures[0]);
        signer.Signed.Should().HaveCount(1);
        (await store.ReadHeadAsync(Segment, CancellationToken.None).ConfigureAwait(false))!
            .LastCheckpointSequence.Should().Be(head.LastSequence);
    }

    [Fact]
    public async Task CutAsync_never_publishes()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedSegment(Segment, count: 3, hash: "ccdd");
        var publisher = new RecordingPublisher();
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptionsWithPublishers(CheckpointPolicies.Never(), typeof(RecordingPublisher)),
            new FixedServiceProvider([(typeof(RecordingPublisher), publisher)]));

        await lifecycle.CutAsync(Segment, null, AuditCheckpointKind.Regular, CancellationToken.None).ConfigureAwait(false);

        publisher.Published.Should().BeEmpty();
        var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        unpublished.Should().HaveCount(1);
    }

    [Fact]
    public async Task CutAsync_retries_a_head_conflict_and_keeps_both_anchors_pending()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedSegment(Segment, count: 4, hash: "eeff");
        store.FailNextAppend();
        var publisher = new RecordingPublisher();
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptionsWithPublishers(CheckpointPolicies.Never(), typeof(RecordingPublisher)),
            new FixedServiceProvider([(typeof(RecordingPublisher), publisher)]));

        var checkpoint = await lifecycle
            .CutAsync(Segment, null, AuditCheckpointKind.Regular, CancellationToken.None)
            .ConfigureAwait(false);

        store.AppendCalls.Should().Be(2);
        publisher.Published.Should().BeEmpty();
        var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        unpublished.Should().HaveCount(2);
        (await store.ReadHeadAsync(Segment, CancellationToken.None).ConfigureAwait(false))!
            .LastCheckpointSequence.Should().Be(checkpoint.Sequence);
    }

    [Fact]
    public async Task CutAsync_rejects_a_segment_without_records()
    {
        var store = new InMemoryAuditChainStore();
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptions(CheckpointPolicies.Never()),
            FixedServiceProvider.Empty);

        var act = () => lifecycle
            .CutAsync(Segment, null, AuditCheckpointKind.Regular, CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task PublishPendingAsync_publishes_every_publisher_and_marks_published()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedSegment(Segment, count: 2, hash: "1122");
        var first = new RecordingPublisher();
        var second = new SecondRecordingPublisher();
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptionsWithPublishers(
                CheckpointPolicies.Never(),
                typeof(RecordingPublisher),
                typeof(SecondRecordingPublisher)),
            new FixedServiceProvider(
            [
                (typeof(RecordingPublisher), first),
                (typeof(SecondRecordingPublisher), second),
            ]));

        await lifecycle.CutAsync(Segment, null, AuditCheckpointKind.Regular, CancellationToken.None).ConfigureAwait(false);
        var published = await lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

        published.Should().Be(1);
        first.Published.Should().HaveCount(1);
        second.Published.Should().HaveCount(1);
        var stored = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        stored.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishPendingAsync_records_a_failure_and_continues_with_the_next_checkpoint()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedSegment("bad", count: 2, hash: "3344");
        store.SeedSegment("good", count: 2, hash: "5566");
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptionsWithPublishers(CheckpointPolicies.Never(), typeof(FailingPublisher)),
            new FixedServiceProvider([(typeof(FailingPublisher), new FailingPublisher("bad"))]));

        await lifecycle.CutAsync("bad", null, AuditCheckpointKind.Regular, CancellationToken.None).ConfigureAwait(false);
        await lifecycle.CutAsync("good", null, AuditCheckpointKind.Regular, CancellationToken.None).ConfigureAwait(false);

        var published = await lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

        published.Should().Be(1);
        var remaining = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        remaining.Should().ContainSingle().Which.Segment.Should().Be("bad");
        remaining[0].PublishAttempts.Should().Be(1);
        remaining[0].LastPublishError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PublishPendingAsync_marks_published_when_no_publisher_is_configured()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedSegment(Segment, count: 2, hash: "7788");
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptions(CheckpointPolicies.Never()),
            FixedServiceProvider.Empty);

        await lifecycle.CutAsync(Segment, null, AuditCheckpointKind.Regular, CancellationToken.None).ConfigureAwait(false);
        var published = await lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

        published.Should().Be(1);
        var remaining = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task CutDueAsync_cuts_where_the_policy_is_due_with_the_policy_label()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedSegment("due", count: 4, hash: "99aa");
        store.SeedSegment("quiet", count: 1, hash: "bbcc");
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptions(CheckpointPolicies.EveryRecords(4, label: "batch")),
            FixedServiceProvider.Empty);

        await lifecycle.CutDueAsync(CancellationToken.None).ConfigureAwait(false);

        var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        unpublished.Should().ContainSingle();
        unpublished[0].Segment.Should().Be("due");
        unpublished[0].Label.Should().Be("batch");
    }

    [Fact]
    public async Task CutDueAsync_continues_past_a_segment_that_cannot_cut()
    {
        var store = new InMemoryAuditChainStore();
        store.SeedHeadWithoutHash("broken", count: 4);
        store.SeedSegment("healthy", count: 4, hash: "ddee");
        var lifecycle = new CheckpointLifecycle<TestAuditDocument>(
            store,
            ChainOptions(CheckpointPolicies.EveryRecords(4)),
            FixedServiceProvider.Empty);

        await lifecycle.CutDueAsync(CancellationToken.None).ConfigureAwait(false);

        var unpublished = await CollectAsync(store.ListUnpublishedAsync(CancellationToken.None)).ConfigureAwait(false);
        unpublished.Should().ContainSingle().Which.Segment.Should().Be("healthy");
    }

    /// <summary>
    ///     A stored audit document for the in-memory chain store.
    /// </summary>
    private sealed class TestAuditDocument : IAuditChainDocument
    {
        /// <inheritdoc />
        public string? Segment { get; set; }

        /// <inheritdoc />
        public long Sequence { get; set; }

        /// <inheritdoc />
        public string? PreviousHash { get; set; }

        /// <inheritdoc />
        public string? Hash { get; set; }

        /// <inheritdoc />
        public string? HashScheme { get; set; }
    }

    /// <summary>
    ///     A clock fixed at one instant, so cut checkpoints carry a known time.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        /// <summary>
        ///     The instant every read returns.
        /// </summary>
        private readonly DateTimeOffset _now;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedTimeProvider" /> class.
        /// </summary>
        /// <param name="now">The instant every read returns.</param>
        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }

    /// <summary>
    ///     A service provider backed by a fixed table.
    /// </summary>
    private sealed class FixedServiceProvider : IServiceProvider
    {
        /// <summary>
        ///     A provider that resolves nothing.
        /// </summary>
        public static readonly FixedServiceProvider Empty = new([]);

        /// <summary>
        ///     The services by implementation type.
        /// </summary>
        private readonly Dictionary<Type, object> _services;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedServiceProvider" /> class.
        /// </summary>
        /// <param name="services">The services by implementation type.</param>
        public FixedServiceProvider((Type Type, object Service)[] services)
        {
            _services = new Dictionary<Type, object>();

            foreach (var (type, service) in services)
            {
                _services[type] = service;
            }
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return _services.TryGetValue(serviceType, out var service) ? service : null;
        }
    }

    /// <summary>
    ///     A signer that returns one fixed signature and records what it signed.
    /// </summary>
    private sealed class FixedSigner : ICheckpointSigner
    {
        /// <summary>
        ///     The checkpoints this signer signed.
        /// </summary>
        private readonly List<AuditCheckpoint> _signed = [];

        /// <summary>
        ///     The fixed signature value.
        /// </summary>
        private readonly string _value;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedSigner" /> class.
        /// </summary>
        /// <param name="value">The fixed signature value.</param>
        public FixedSigner(string value)
        {
            _value = value;
        }

        /// <summary>
        ///     Gets the checkpoints this signer signed.
        /// </summary>
        public IReadOnlyList<AuditCheckpoint> Signed => _signed;

        /// <inheritdoc />
        public ValueTask<AuditCheckpointSignature> SignAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            _signed.Add(checkpoint);

            return ValueTask.FromResult(new AuditCheckpointSignature("test-key", _value));
        }

        /// <inheritdoc />
        public ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    ///     A publisher that records what it published.
    /// </summary>
    private class RecordingPublisher : ICheckpointPublisher
    {
        /// <summary>
        ///     The checkpoints this publisher received.
        /// </summary>
        private readonly List<AuditCheckpoint> _published = [];

        /// <summary>
        ///     Gets the checkpoints this publisher received.
        /// </summary>
        public IReadOnlyList<AuditCheckpoint> Published => _published;

        /// <inheritdoc />
        public virtual ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            _published.Add(checkpoint);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     A second recording publisher type, so two publishers can register distinctly.
    /// </summary>
    private sealed class SecondRecordingPublisher : RecordingPublisher
    {
    }

    /// <summary>
    ///     A publisher that fails for one segment and records the rest.
    /// </summary>
    private sealed class FailingPublisher : ICheckpointPublisher
    {
        /// <summary>
        ///     The segment this publisher fails for.
        /// </summary>
        private readonly string _failingSegment;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FailingPublisher" /> class.
        /// </summary>
        /// <param name="failingSegment">The segment this publisher fails for.</param>
        public FailingPublisher(string failingSegment)
        {
            _failingSegment = failingSegment;
        }

        /// <inheritdoc />
        public ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            if (string.Equals(checkpoint.Segment, _failingSegment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The anchor store rejected segment '{_failingSegment}'.");
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     An in-memory chain store with head version checks, so cuts and publishes run without a database.
    /// </summary>
    private sealed class InMemoryAuditChainStore : IAuditChainStore<TestAuditDocument>
    {
        /// <summary>
        ///     Guards every collection below.
        /// </summary>
        private readonly object _gate = new();

        /// <summary>
        ///     The heads by segment.
        /// </summary>
        private readonly Dictionary<string, AuditSegmentHead> _heads = new(StringComparer.Ordinal);

        /// <summary>
        ///     The documents by segment.
        /// </summary>
        private readonly Dictionary<string, List<TestAuditDocument>> _documents = new(StringComparer.Ordinal);

        /// <summary>
        ///     The checkpoints by id.
        /// </summary>
        private readonly Dictionary<Guid, AuditCheckpoint> _checkpoints = [];

        /// <summary>
        ///     Whether the next head advance fails with a conflict.
        /// </summary>
        private bool _failNextAppend;

        /// <summary>
        ///     Gets how many head advances ran.
        /// </summary>
        public int AppendCalls { get; private set; }

        /// <summary>
        ///     Seeds a segment with linked documents and returns its head.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="count">How many documents to seed.</param>
        /// <param name="hash">The head hash.</param>
        /// <returns>The seeded head.</returns>
        public AuditSegmentHead SeedSegment(string segment, int count, string hash)
        {
            var info = new AuditSegmentInfo(
                segment,
                null,
                new Dictionary<string, string>(),
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
            var head = new AuditSegmentHead(info, count, hash, count, null, null, false, 1);
            List<TestAuditDocument> documents = new(count);

            for (var sequence = 1; sequence <= count; sequence++)
            {
                documents.Add(new TestAuditDocument
                {
                    Segment = segment,
                    Sequence = sequence,
                    PreviousHash = sequence == 1 ? null : "prev",
                    Hash = sequence == count ? hash : "prev",
                    HashScheme = "sha256-v2",
                });
            }

            lock (_gate)
            {
                _heads[segment] = head;
                _documents[segment] = documents;
            }

            return head;
        }

        /// <summary>
        ///     Seeds a head that holds records but no hash, so its cut fails.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="count">How many records the head claims.</param>
        public void SeedHeadWithoutHash(string segment, int count)
        {
            var info = new AuditSegmentInfo(
                segment,
                null,
                new Dictionary<string, string>(),
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

            lock (_gate)
            {
                _heads[segment] = new AuditSegmentHead(info, count, null, count, null, null, false, 1);
            }
        }

        /// <summary>
        ///     Fails the next head advance with a conflict, so the cut retries.
        /// </summary>
        public void FailNextAppend()
        {
            lock (_gate)
            {
                _failNextAppend = true;
            }
        }

        /// <inheritdoc />
        public ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return ValueTask.FromResult(_heads.TryGetValue(segment, out var head) ? head : null);
            }
        }

        /// <inheritdoc />
        public ValueTask AppendAsync(
            string segment,
            AuditSegmentHead expected,
            IReadOnlyList<TestAuditDocument> documents,
            AuditSegmentHead updated,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                AppendCalls++;

                if (_failNextAppend)
                {
                    _failNextAppend = false;

                    throw new AuditHeadConflictException($"The head of segment '{segment}' changed while it was cut.");
                }

                if (!_heads.TryGetValue(segment, out var stored))
                {
                    if (expected.Version != 0)
                    {
                        throw new AuditHeadConflictException($"The head of segment '{segment}' changed while it was written.");
                    }

                    _heads[segment] = updated;

                    return ValueTask.CompletedTask;
                }

                if (stored.Version != expected.Version)
                {
                    throw new AuditHeadConflictException($"The head of segment '{segment}' changed while it was written.");
                }

                _heads[segment] = updated;

                return ValueTask.CompletedTask;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<TestAuditDocument> ListUnlinkedAsync(
            string segment,
            TimeSpan? olderThan,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
            TimeSpan? olderThan,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<TestAuditDocument> ReadSegmentAsync(
            string segment,
            long fromSequence,
            long? toSequence,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            List<TestAuditDocument> snapshot;

            lock (_gate)
            {
                snapshot = _documents.TryGetValue(segment, out var stored) ? [.. stored] : [];
            }

            foreach (var document in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (document.Sequence < fromSequence || (toSequence is not null && document.Sequence > toSequence.Value))
                {
                    continue;
                }

                yield return document;

                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
            AuditSegmentFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            List<AuditSegmentHead> snapshot;

            lock (_gate)
            {
                snapshot = [.. _heads.Values];
            }

            foreach (var head in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!filter.IncludeExpired && head.Expired)
                {
                    continue;
                }

                if (filter.TenantId is not null && !string.Equals(head.Info.TenantId, filter.TenantId, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return head;

                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _checkpoints[checkpoint.Id] = checkpoint;

                return ValueTask.CompletedTask;
            }
        }

        /// <inheritdoc />
        public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return ValueTask.FromResult(_checkpoints.TryGetValue(checkpointId, out var checkpoint) ? checkpoint : null);
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            List<AuditCheckpoint> snapshot;

            lock (_gate)
            {
                snapshot = [];

                foreach (var checkpoint in _checkpoints.Values)
                {
                    if (checkpoint.PublishState == CheckpointPublishState.Pending)
                    {
                        snapshot.Add(checkpoint);
                    }
                }
            }

            foreach (var checkpoint in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return checkpoint;

                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_checkpoints.TryGetValue(checkpointId, out var stored))
                {
                    throw new InvalidOperationException($"No stored checkpoint carries id '{checkpointId}'.");
                }

                _checkpoints[checkpointId] = stored with
                {
                    PublishState = publishError is null ? CheckpointPublishState.Published : CheckpointPublishState.Pending,
                    PublishAttempts = stored.PublishAttempts + 1,
                    LastPublishError = publishError,
                };

                return ValueTask.CompletedTask;
            }
        }

        /// <inheritdoc />
        public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
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

                return ValueTask.CompletedTask;
            }
        }
    }
}
