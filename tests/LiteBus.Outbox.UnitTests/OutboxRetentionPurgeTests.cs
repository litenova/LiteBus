using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies manual retention purge rejects unconfigured retention policies.
/// </summary>
public sealed class OutboxRetentionPurgeTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a purge without retention throws and never calls the store.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunRetentionPurgeAsync_when_retention_missing_or_zero_should_throw(bool zero)
    {
        var store = new InMemoryOutboxStore();
        var retentionStore = new RecordingRetentionStore();
        var options = new OutboxCleanupHostOptions { Retention = zero ? TimeSpan.Zero : null };
        var manager = new OutboxManager(
            store,
            retentionStore,
            new OutboxRetentionCoordinator(options),
            options,
            new ManualTimeProvider(BaseTime));

        var act = () => manager.RunRetentionPurgeAsync();

        var exception = await act.Should().ThrowAsync<OutboxManagementException>().ConfigureAwait(false);
        exception.Which.Message.Should().Contain("OutboxCleanupHostOptions.Retention");
        retentionStore.CallCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a purge with non-positive retention throws.
    /// </summary>
    [Fact]
    public async Task RunRetentionPurgeAsync_when_retention_negative_should_throw()
    {
        var store = new InMemoryOutboxStore();
        var retentionStore = new RecordingRetentionStore();
        var options = new OutboxCleanupHostOptions { Retention = TimeSpan.FromHours(-1) };
        var manager = new OutboxManager(
            store,
            retentionStore,
            new OutboxRetentionCoordinator(options),
            options,
            new ManualTimeProvider(BaseTime));

        var act = () => manager.RunRetentionPurgeAsync();

        await act.Should().ThrowAsync<OutboxManagementException>().ConfigureAwait(false);
        retentionStore.CallCount.Should().Be(0);
    }

    private sealed class RecordingRetentionStore : IOutboxRetentionStore
    {
        public int CallCount { get; private set; }

        public Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(0);
        }
    }
}
