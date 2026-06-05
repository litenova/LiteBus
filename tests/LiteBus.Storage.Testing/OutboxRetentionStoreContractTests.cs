using LiteBus.Outbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared retention contract tests for stores that support bulk delete of published rows.
/// </summary>
public abstract class OutboxRetentionStoreContractTests : OutboxStoreContractTests
{
    /// <summary>
    ///     Verifies that published rows older than the retention cutoff are deleted.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DeletePublishedOlderThanAsync_ShouldRemoveEligibleRows()
    {
        var store = CreateStore();
        var retainedId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(retainedId, now));
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(deletedId, now.AddHours(-2)));

        await store.TerminalState.MarkPublishedAsync(retainedId);
        await store.TerminalState.MarkPublishedAsync(deletedId);

        var deleted = await store.Retention.DeletePublishedOlderThanAsync(now.AddHours(-1));

        deleted.Should().Be(1);

        var counts = await store.Diagnostics.GetStatusCountsAsync();
        counts.Should().ContainKey(OutboxStatus.Published).WhoseValue.Should().Be(1);
    }
}
