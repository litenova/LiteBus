using LiteBus.Inbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared retention contract tests for stores that support bulk delete of completed rows.
/// </summary>
public abstract class InboxRetentionStoreContractTests : InboxStoreContractTests
{
    /// <summary>
    ///     Verifies that completed rows older than the retention cutoff are deleted.
    /// </summary>
    [Fact]
    public async Task DeleteCompletedOlderThanAsync_ShouldRemoveEligibleRows()
    {
        var roles = CreateStore();
        var retainedId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(retainedId, now));
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(deletedId, now.AddHours(-2)));

        await roles.TerminalStateStore.MarkCompletedAsync(retainedId);
        await roles.TerminalStateStore.MarkCompletedAsync(deletedId);

        var deleted = await roles.RetentionStore.DeleteCompletedOlderThanAsync(now.AddHours(-1));

        deleted.Should().Be(1);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync();
        counts.Should().ContainKey(InboxStatus.Completed).WhoseValue.Should().Be(1);
    }
}
