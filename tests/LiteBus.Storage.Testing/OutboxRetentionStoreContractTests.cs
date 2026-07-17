using LiteBus.Outbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared retention contract tests for stores that support bulk delete of published rows.
/// </summary>
public abstract class OutboxRetentionStoreContractTests
{
    /// <summary>
    ///     Gets the UTC timestamp used as a stable clock for retention cutoff assertions.
    /// </summary>
    protected virtual DateTimeOffset BaseTime { get; } = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Creates a store that implements the writer, lease, state, retention, and diagnostics roles for one test run.
    /// </summary>
    /// <returns>The store contracts under test.</returns>
    protected abstract OutboxStoreContracts CreateStore();

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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(retainedId, now)).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(deletedId, now.AddHours(-2))).ConfigureAwait(false);

        var retainedLease = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([retainedLease[0].AsPublished(now)]).ConfigureAwait(false);

        var deletedLease = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-2",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([deletedLease[0].AsPublished(now.AddHours(-2))]).ConfigureAwait(false);

        var deleted = await store.Retention.DeletePublishedOlderThanAsync(now.AddHours(-1)).ConfigureAwait(false);

        deleted.Should().Be(1);

        var counts = await store.Diagnostics.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Should().ContainKey(OutboxStatus.Published).WhoseValue.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that retention uses <c>published_at</c> when set instead of falling back to <c>created_at</c>.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DeletePublishedOlderThanAsync_WhenPublishedAtIsRecentButCreatedAtIsOld_ShouldRetainRow()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var createdAt = BaseTime.AddDays(-30);
        var publishedAt = BaseTime;
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, createdAt)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsPublished(publishedAt)]).ConfigureAwait(false);

        var deleted = await store.Retention.DeletePublishedOlderThanAsync(now.AddDays(-1)).ConfigureAwait(false);

        deleted.Should().Be(0);

        var counts = await store.Diagnostics.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Should().ContainKey(OutboxStatus.Published).WhoseValue.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that no rows are deleted when the cutoff is earlier than every published row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DeletePublishedOlderThanAsync_WhenCutoffIsBeforeAllRows_ShouldDeleteNothing()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);

        var deleted = await store.Retention.DeletePublishedOlderThanAsync(now.AddHours(-2)).ConfigureAwait(false);

        deleted.Should().Be(0);

        var counts = await store.Diagnostics.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Should().ContainKey(OutboxStatus.Published).WhoseValue.Should().Be(1);
    }

    /// <summary>
    ///     Creates a pending envelope for retention contract tests.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    /// <returns>A pending envelope.</returns>
    protected static OutboxEnvelope CreatePendingEnvelope(Guid messageId, DateTimeOffset createdAt)
    {
        return new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }

    /// <summary>
    ///     Holds the outbox store roles exercised by retention contract tests.
    /// </summary>
    /// <param name="Writer">The writer role.</param>
    /// <param name="Lease">The lease role.</param>
    /// <param name="StateWriter">The state writer role.</param>
    /// <param name="Retention">The retention role.</param>
    /// <param name="Diagnostics">The diagnostics role.</param>
    public sealed record OutboxStoreContracts(
        IOutboxStore Writer,
        IOutboxLeaseStore Lease,
        IOutboxStateWriter StateWriter,
        IOutboxRetentionStore Retention,
        IOutboxDiagnosticsStore Diagnostics);
}
