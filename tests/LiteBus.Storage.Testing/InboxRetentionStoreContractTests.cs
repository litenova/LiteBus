using LiteBus.Inbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared retention contract tests for stores that support bulk delete of completed rows.
/// </summary>
public abstract class InboxRetentionStoreContractTests
{
    /// <summary>
    ///     Gets a fixed UTC timestamp used as the baseline for retention cutoff assertions.
    /// </summary>
    protected static DateTimeOffset BaseTime { get; } = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Creates a fresh store instance for one retention test.
    /// </summary>
    /// <returns>The writer, lease, state, retention, and diagnostics roles backed by the same store instance.</returns>
    protected abstract InboxStoreRoles CreateStore();

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

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(retainedId, now)).ConfigureAwait(false);
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(deletedId, now.AddHours(-2))).ConfigureAwait(false);

        var retainedLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await roles.StateWriter.PersistAsync([retainedLease[0].AsCompleted() with { CompletedAt = now }]).ConfigureAwait(false);

        var deletedLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await roles.StateWriter.PersistAsync([deletedLease[0].AsCompleted() with { CompletedAt = now.AddHours(-2) }]).ConfigureAwait(false);

        var deleted = await roles.RetentionStore.DeleteCompletedOlderThanAsync(now.AddHours(-1)).ConfigureAwait(false);

        deleted.Should().Be(1);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Should().ContainKey(InboxStatus.Completed).WhoseValue.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that retention uses <c>completed_at</c> when set instead of falling back to <c>created_at</c>.
    /// </summary>
    [Fact]
    public async Task DeleteCompletedOlderThanAsync_WhenCompletedAtIsRecentButCreatedAtIsOld_ShouldRetainRow()
    {
        var roles = CreateStore();
        var messageId = Guid.NewGuid();
        var createdAt = BaseTime.AddDays(-30);
        var completedAt = BaseTime;
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, createdAt)).ConfigureAwait(false);

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await roles.StateWriter.PersistAsync([leased[0].AsCompleted() with { CompletedAt = completedAt }]).ConfigureAwait(false);

        var deleted = await roles.RetentionStore.DeleteCompletedOlderThanAsync(now.AddDays(-1)).ConfigureAwait(false);

        deleted.Should().Be(0);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Should().ContainKey(InboxStatus.Completed).WhoseValue.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that no rows are deleted when the cutoff is earlier than every completed row.
    /// </summary>
    [Fact]
    public async Task DeleteCompletedOlderThanAsync_WhenCutoffIsBeforeAllRows_ShouldDeleteNothing()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now)).ConfigureAwait(false);

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await roles.StateWriter.PersistAsync([leased[0].AsCompleted()]).ConfigureAwait(false);

        var deleted = await roles.RetentionStore.DeleteCompletedOlderThanAsync(now.AddHours(-2)).ConfigureAwait(false);

        deleted.Should().Be(0);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Should().ContainKey(InboxStatus.Completed).WhoseValue.Should().Be(1);
    }

    /// <summary>
    ///     Creates a pending envelope for retention contract tests.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="createdAt">The storage timestamp.</param>
    /// <returns>A pending envelope ready for append.</returns>
    protected static InboxEnvelope CreatePendingEnvelope(Guid commandId, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };
    }

    /// <summary>
    ///     Holds the inbox store roles exercised by retention contract tests.
    /// </summary>
    /// <param name="Writer">The append-only writer role.</param>
    /// <param name="LeaseStore">The lease role used by the processor.</param>
    /// <param name="StateWriter">The state writer role used by the processor.</param>
    /// <param name="RetentionStore">The retention role used by cleanup.</param>
    /// <param name="DiagnosticsStore">The diagnostics role used by operators.</param>
    public sealed record InboxStoreRoles(
        IInboxStore Writer,
        IInboxLeaseStore LeaseStore,
        IInboxStateWriter StateWriter,
        IInboxRetentionStore RetentionStore,
        IInboxDiagnosticsStore DiagnosticsStore);
}