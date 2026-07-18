using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies outbox retention cleanup background service behavior.
/// </summary>
public sealed class OutboxCleanupBackgroundServiceTests
{
    /// <summary>
    ///     Verifies nonpositive cleanup timing is rejected instead of creating a tight store loop.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_WhenCleanupTimingIsNonpositive_ShouldThrow(bool retention)
    {
        var options = new OutboxCleanupHostOptions
        {
            Interval = retention ? TimeSpan.FromMinutes(1) : TimeSpan.Zero,
            Retention = retention ? TimeSpan.Zero : TimeSpan.FromMinutes(1)
        };

        var act = options.Validate;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies cleanup deletes published rows older than the configured retention window.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteAsync_ShouldDeletePublishedRowsOlderThanRetention()
    {
        var now = new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var store = new InMemoryOutboxStore(timeProvider: clock);
        var options = new OutboxCleanupHostOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromMilliseconds(25),
            Retention = TimeSpan.FromHours(1)
        };
        var coordinator = new OutboxRetentionCoordinator(options);
        var oldPublishedId = Guid.NewGuid();
        var recentPublishedId = Guid.NewGuid();
        await store.AddAsync(CreateEnvelope(oldPublishedId, now.AddHours(-3))).ConfigureAwait(false);
        await store.AddAsync(CreateEnvelope(recentPublishedId, now.AddMinutes(-10))).ConfigureAwait(false);
        var cleanup = new OutboxCleanupBackgroundService(store, options, clock, coordinator);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await cleanup.ExecuteAsync(cancellation.Token).ConfigureAwait(false);

        store.GetAll(OutboxStatus.Published).Should().ContainSingle(envelope => envelope.Id == recentPublishedId);
        store.GetAll(OutboxStatus.Published).Should().NotContain(envelope => envelope.Id == oldPublishedId);
        coordinator.GetStatus().LastError.Should().BeNull();
    }

    private static OutboxEnvelope CreateEnvelope(Guid id, DateTimeOffset createdAt)
    {
        return new OutboxEnvelope
        {
            Id = id,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            Status = OutboxStatus.Published,
            AttemptCount = 1,
            PublishedAt = createdAt
        };
    }
}
