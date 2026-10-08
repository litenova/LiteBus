using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox retention cleanup background service behavior.
/// </summary>
public sealed class InboxCleanupBackgroundServiceTests
{
    /// <summary>
    ///     Verifies nonpositive cleanup timing is rejected instead of creating a tight store loop.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_WhenCleanupTimingIsNonpositive_ShouldThrow(bool retention)
    {
        var options = new InboxCleanupHostOptions
        {
            Interval = retention ? TimeSpan.FromMinutes(1) : TimeSpan.Zero,
            Retention = retention ? TimeSpan.Zero : TimeSpan.FromMinutes(1)
        };

        var act = options.Validate;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies cleanup deletes completed rows older than the configured retention window.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldDeleteCompletedRowsOlderThanRetention()
    {
        var now = new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var store = new InMemoryInboxStore(timeProvider: clock);

        var options = new InboxCleanupHostOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromMilliseconds(50),
            Retention = TimeSpan.FromHours(1)
        };

        var coordinator = new InboxRetentionCoordinator(options);

        var oldCompletedId = Guid.NewGuid();
        var recentCompletedId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(oldCompletedId, now.AddHours(-3), InboxStatus.Completed)).ConfigureAwait(false);
        await store.AddAsync(CreateEnvelope(recentCompletedId, now.AddMinutes(-10), InboxStatus.Completed)).ConfigureAwait(false);

        var cleanup = new InboxCleanupBackgroundService(
            store,
            options,
            clock,
            coordinator);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await cleanup.ExecuteAsync(cts.Token).ConfigureAwait(false);

        store.GetAll(InboxStatus.Completed).Should().ContainSingle(envelope => envelope.Id == recentCompletedId);
        store.GetAll(InboxStatus.Completed).Should().NotContain(envelope => envelope.Id == oldCompletedId);
        coordinator.GetStatus().LastError.Should().BeNull();
    }

    /// <summary>
    ///     Creates a test inbox envelope.
    /// </summary>
    /// <param name="id">The message identifier.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    /// <param name="status">The inbox status.</param>
    /// <returns>The envelope.</returns>
    /// <summary>
    ///     Verifies host cancellation during the error backoff ends the loop without surfacing cancellation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringErrorBackoff_ShouldCompleteWithoutThrowing()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero));
        var options = new InboxCleanupHostOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromMinutes(5),
            Retention = TimeSpan.FromHours(1)
        };
        var coordinator = new InboxRetentionCoordinator(options);
        var cleanup = new InboxCleanupBackgroundService(new ThrowingRetentionStore(), options, clock, coordinator);

        using var cancellation = new CancellationTokenSource();
        var execution = cleanup.ExecuteAsync(cancellation.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (coordinator.GetStatus().LastError is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        coordinator.GetStatus().LastError.Should().NotBeNull();
        await cancellation.CancelAsync().ConfigureAwait(false);
        await execution.ConfigureAwait(false);

        coordinator.GetStatus().LastError.Should().Contain("retention store unavailable");
    }

    /// <summary>
    ///     Retention store that fails every delete so the error backoff path can be observed.
    /// </summary>
    private sealed class ThrowingRetentionStore : IInboxRetentionStore
    {
        /// <inheritdoc />
        public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
        {
            return Task.FromException<int>(new IOException("retention store unavailable"));
        }
    }

    private static InboxEnvelope CreateEnvelope(Guid id, DateTimeOffset createdAt, InboxStatus status)
    {
        return new InboxEnvelope
        {
            Id = id,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            Status = status,
            AttemptCount = 0,
            CompletedAt = status == InboxStatus.Completed ? createdAt : null
        };
    }
}
