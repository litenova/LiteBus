using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies completion timestamps come from the processor clock instead of the wall clock.
/// </summary>
public sealed class InboxCompletionClockReleaseFixesTests
{
    /// <summary>
    ///     Gets the fixed clock time used by completion assertions.
    /// </summary>
    private static readonly DateTimeOffset FixedTime = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies the completion transition stamps the supplied timestamp.
    /// </summary>
    [Fact]
    public void AsCompleted_WhenTimestampSupplied_ShouldStampCompletedAt()
    {
        var completed = CreateProcessingEnvelope().AsCompleted(FixedTime);

        completed.Status.Should().Be(InboxStatus.Completed);
        completed.CompletedAt.Should().Be(FixedTime);
    }

    /// <summary>
    ///     Verifies the completion transition without a timestamp leaves stamping to the store.
    /// </summary>
    [Fact]
    public void AsCompleted_WhenTimestampOmitted_ShouldLeaveCompletedAtUnset()
    {
        var completed = CreateProcessingEnvelope().AsCompleted();

        completed.Status.Should().Be(InboxStatus.Completed);
        completed.CompletedAt.Should().BeNull();
    }

    /// <summary>
    ///     Verifies successful dispatch stamps completion from the processor clock.
    /// </summary>
    /// <returns>A task that completes when the clock assertion succeeds.</returns>
    [Fact]
    public async Task ProcessAsync_WhenDispatchSucceeds_ShouldStampProcessorClock()
    {
        var completed = await InboxProcessorEnvelopeHandler.ProcessAsync(
            CreateProcessingEnvelope(),
            new SucceedingDispatcher(),
            new InboxProcessorOptions(),
            new FixedTimeProvider(FixedTime),
            NullLogger.Instance,
            [],
            CancellationToken.None).ConfigureAwait(false);

        completed.Should().NotBeNull();
        completed!.CompletedAt.Should().Be(FixedTime);
    }

    /// <summary>
    ///     Verifies the in-memory store stamps unstamped completions from its injected clock.
    /// </summary>
    /// <returns>A task that completes when the clock assertion succeeds.</returns>
    [Fact]
    public async Task PersistAsync_WhenCompletedAtUnset_ShouldStampInjectedClock()
    {
        var store = new InMemoryInboxStore(options: null, timeProvider: new FixedTimeProvider(FixedTime));
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = FixedTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "clock-test",
            Now = FixedTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        var result = await store.PersistAsync([leased[0].AsCompleted()]).ConfigureAwait(false);

        result.AppliedCount.Should().Be(1);
        store.Get(messageId).CompletedAt.Should().Be(FixedTime);
    }

    /// <summary>
    ///     Creates a processing envelope for completion transition tests.
    /// </summary>
    /// <returns>A leased inbox envelope.</returns>
    private static InboxEnvelope CreateProcessingEnvelope()
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = FixedTime,
            AttemptCount = 1,
            Status = InboxStatus.Processing,
            LeaseOwner = "clock-test",
            LeaseGeneration = 1
        };
    }

    /// <summary>
    ///     Dispatcher test double that completes every dispatch successfully.
    /// </summary>
    private sealed class SucceedingDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Time provider test double that always reports one fixed timestamp.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        /// <summary>
        ///     Gets the fixed UTC timestamp reported by this provider.
        /// </summary>
        private readonly DateTimeOffset _fixedTime;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedTimeProvider" /> class.
        /// </summary>
        /// <param name="fixedTime">The fixed UTC timestamp.</param>
        public FixedTimeProvider(DateTimeOffset fixedTime)
        {
            _fixedTime = fixedTime;
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow()
        {
            return _fixedTime;
        }
    }
}
