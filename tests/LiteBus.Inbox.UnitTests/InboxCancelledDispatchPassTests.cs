using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies a dispatcher that throws on cancellation leaves the pass failed count at zero and the row unchanged.
/// </summary>
public sealed class InboxCancelledDispatchPassTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a cancelled dispatch records no failed outcome in the pass accumulator.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_WhenDispatcherThrowsOnCancellation_ShouldLeaveFailedCountAtZero()
    {
        using var cancellation = new CancellationTokenSource();
        var accumulator = new ProcessorPassAccumulator<InboxEnvelope>();

        var updated = await InboxProcessorEnvelopeHandler.ProcessAsync(
            CreateLeasedEnvelope(),
            new CancellingInboxDispatcher(cancellation),
            new InboxProcessorOptions { Retry = new RetryOptions { UseJitter = false } },
            TimeProvider.System,
            accumulator,
            NullLogger.Instance,
            [],
            cancellation.Token).ConfigureAwait(false);

        updated.Should().BeNull();

        var result = accumulator.ToResult(1, TimeSpan.Zero);
        result.FailedCount.Should().Be(0);
        result.SucceededCount.Should().Be(0);
        result.DeadLetteredCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a cancelled pass leaves the stored row in its pending state.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherThrowsOnCancellation_ShouldLeaveRowUnchanged()
    {
        var store = new InMemoryInboxStore();
        using var cancellation = new CancellationTokenSource();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CancellingInboxDispatcher(cancellation),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "cancellation-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var act = () => processor.ProcessPendingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var stored = store.Get(messageId);
        stored.AttemptCount.Should().Be(0);
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.Status.Should().Be(InboxStatus.Pending);
        stored.LastError.Should().BeNull();
        stored.ErrorType.Should().BeNull();
        stored.FirstFailedAt.Should().BeNull();
        stored.VisibleAfter.Should().BeNull();
    }

    private static InboxEnvelope CreateLeasedEnvelope()
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 1,
            Status = InboxStatus.Processing
        };
    }

    private sealed class CancellingInboxDispatcher : IInboxDispatcher
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingInboxDispatcher(CancellationTokenSource cancellation)
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            _cancellation = cancellation;
        }

        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();

            throw new OperationCanceledException(_cancellation.Token);
        }
    }
}
