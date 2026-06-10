using LiteBus.Inbox;
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

        await store.AddAsync(CreateEnvelope(oldCompletedId, now.AddHours(-3), InboxStatus.Completed));
        await store.AddAsync(CreateEnvelope(recentCompletedId, now.AddMinutes(-10), InboxStatus.Completed));

        var cleanup = new InboxCleanupBackgroundService(
            store,
            options,
            clock,
            coordinator);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await cleanup.ExecuteAsync(cts.Token);

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
