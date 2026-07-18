using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox manager pagination, diagnostics, purge, and retention behavior.
/// </summary>
public sealed class InboxManagerOperationsTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies query filters, missing lookup behavior, status counts, and logical schema information.
    /// </summary>
    [Fact]
    public async Task BrowseAndDiagnostics_ReflectStoredRows()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);
        var matchingId = Guid.NewGuid();

        await store.AddBatchAsync([
            CreateEnvelope(matchingId, InboxStatus.DeadLettered, "tenant-a"),
            CreateEnvelope(Guid.NewGuid(), InboxStatus.Pending, "tenant-b")
        ]).ConfigureAwait(false);

        var page = await manager.QueryAsync(
            new InboxMessageFilter
            {
                Statuses = [InboxStatus.DeadLettered],
                TenantId = "tenant-a"
            },
            new InboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var missing = await manager.GetMessageAsync(Guid.NewGuid()).ConfigureAwait(false);
        var counts = await manager.GetStatusCountsAsync().ConfigureAwait(false);
        var schema = await manager.GetSchemaInfoAsync().ConfigureAwait(false);

        page.Items.Should().ContainSingle(item => item.Id == matchingId);
        missing.Should().BeNull();
        counts[InboxStatus.DeadLettered].Should().Be(1);
        counts[InboxStatus.Pending].Should().Be(1);
        schema.Component.Should().Be("inbox");
        schema.RecordedVersion.Should().Be(schema.ExpectedVersion);
    }

    /// <summary>
    ///     Verifies bulk dead-letter replay follows keyset pagination beyond the internal page size.
    /// </summary>
    [Fact]
    public async Task RequeueDeadLettersAsync_WhenMoreThanOnePage_RequeuesEveryRow()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);
        var envelopes = Enumerable.Range(0, 201)
            .Select(index => CreateEnvelope(
                Guid.NewGuid(),
                InboxStatus.DeadLettered,
                null,
                BaseTime.AddTicks(index)))
            .ToArray();

        await store.AddBatchAsync(envelopes).ConfigureAwait(false);

        var requeued = await manager.RequeueDeadLettersAsync().ConfigureAwait(false);

        requeued.Should().Be(201);
        store.GetAll(InboxStatus.DeadLettered).Should().BeEmpty();
        store.GetAll(InboxStatus.Pending).Should().HaveCount(201);
    }

    /// <summary>
    ///     Verifies empty replay requests do not call the store and report zero requested rows.
    /// </summary>
    [Fact]
    public async Task RequeueOperations_WhenNoRows_ReturnZeroCounts()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);

        var bulkCount = await manager.RequeueDeadLettersAsync().ConfigureAwait(false);
        var selected = await manager.RequeueAsync([]).ConfigureAwait(false);

        bulkCount.Should().Be(0);
        selected.Should().Be(new RequeueResult(0, 0));
    }

    /// <summary>
    ///     Verifies narrowed purge needs no confirmation and confirmed unrestricted purge removes the remainder.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithNarrowingOrConfirmation_RemovesMatchingRows()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);

        await store.AddBatchAsync([
            CreateEnvelope(Guid.NewGuid(), InboxStatus.Completed, "tenant-a"),
            CreateEnvelope(Guid.NewGuid(), InboxStatus.Pending, "tenant-b")
        ]).ConfigureAwait(false);

        var narrowed = await manager.PurgeAsync(new InboxMessageFilter { TenantId = "tenant-a" })
            .ConfigureAwait(false);
        var unrestricted = await manager.PurgeAsync(new InboxMessageFilter(), confirm: true)
            .ConfigureAwait(false);

        narrowed.Should().Be(1);
        unrestricted.Should().Be(1);
        store.Count.Should().Be(0);
    }

    /// <summary>
    ///     Verifies disabled retention policies return without invoking the retention store.
    /// </summary>
    [Fact]
    public async Task RunRetentionPurgeAsync_WhenRetentionIsMissingOrZero_DoesNotCallStore()
    {
        var store = new InMemoryInboxStore();
        var retentionStore = new RecordingRetentionStore();

        var missing = CreateManager(
            store,
            new InboxCleanupHostOptions { Retention = null },
            retentionStore,
            new ManualTimeProvider(BaseTime));
        var zero = CreateManager(
            store,
            new InboxCleanupHostOptions { Retention = TimeSpan.Zero },
            retentionStore,
            new ManualTimeProvider(BaseTime));

        (await missing.RunRetentionPurgeAsync().ConfigureAwait(false)).Should().Be(0);
        (await zero.RunRetentionPurgeAsync().ConfigureAwait(false)).Should().Be(0);
        retentionStore.CallCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies local retention operations honor cancellation before reading status or returning disabled results.
    /// </summary>
    [Fact]
    public async Task RetentionOperations_WhenCancellationIsRequested_ShouldThrow()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var readStatus = () => manager.GetRetentionStatusAsync(cancellationSource.Token);
        var runPurge = () => manager.RunRetentionPurgeAsync(cancellationSource.Token);

        await readStatus.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        await runPurge.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies successful retention uses the configured cutoff and records the run result.
    /// </summary>
    [Fact]
    public async Task RunRetentionPurgeAsync_WhenStoreSucceeds_RecordsCutoffAndStatus()
    {
        var store = new InMemoryInboxStore();
        var options = new InboxCleanupHostOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromMinutes(15),
            Retention = TimeSpan.FromDays(2)
        };
        var retentionStore = new RecordingRetentionStore { Result = 7 };
        var manager = CreateManager(store, options, retentionStore, new ManualTimeProvider(BaseTime));

        var deleted = await manager.RunRetentionPurgeAsync().ConfigureAwait(false);
        var status = await manager.GetRetentionStatusAsync().ConfigureAwait(false);

        deleted.Should().Be(7);
        retentionStore.Cutoff.Should().Be(BaseTime.AddDays(-2));
        status.Enabled.Should().BeTrue();
        status.Retention.Should().Be(TimeSpan.FromDays(2));
        status.Interval.Should().Be(TimeSpan.FromMinutes(15));
        status.LastRunAt.Should().Be(BaseTime);
        status.LastDeletedCount.Should().Be(7);
        status.LastError.Should().BeNull();
    }

    /// <summary>
    ///     Verifies retention failures are recorded and still propagate to the caller.
    /// </summary>
    [Fact]
    public async Task RunRetentionPurgeAsync_WhenStoreFails_RecordsFailureAndRethrows()
    {
        var store = new InMemoryInboxStore();
        var options = new InboxCleanupHostOptions { Retention = TimeSpan.FromDays(1) };
        var retentionStore = new RecordingRetentionStore
        {
            Exception = new InvalidOperationException("retention unavailable")
        };
        var manager = CreateManager(store, options, retentionStore, new ManualTimeProvider(BaseTime));

        var action = () => manager.RunRetentionPurgeAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("retention unavailable").ConfigureAwait(false);

        var status = await manager.GetRetentionStatusAsync().ConfigureAwait(false);
        status.LastRunAt.Should().Be(BaseTime);
        status.LastDeletedCount.Should().Be(0);
        status.LastError.Should().Be("retention unavailable");
    }

    private static InboxEnvelope CreateEnvelope(
        Guid id,
        InboxStatus status,
        string? tenantId,
        DateTimeOffset? createdAt = null)
    {
        return new InboxEnvelope
        {
            Id = id,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt ?? BaseTime,
            CompletedAt = status == InboxStatus.Completed ? createdAt ?? BaseTime : null,
            Status = status,
            AttemptCount = 0,
            TenantId = tenantId
        };
    }

    private static InboxManager CreateManager(InMemoryInboxStore store)
    {
        var options = new InboxCleanupHostOptions();
        return CreateManager(store, options, store, TimeProvider.System);
    }

    private static InboxManager CreateManager(
        InMemoryInboxStore store,
        InboxCleanupHostOptions options,
        IInboxRetentionStore retentionStore,
        TimeProvider timeProvider)
    {
        return new InboxManager(
            store,
            retentionStore,
            new InboxRetentionCoordinator(options),
            options,
            timeProvider);
    }

    private sealed class RecordingRetentionStore : IInboxRetentionStore
    {
        internal int CallCount { get; private set; }

        internal DateTimeOffset? Cutoff { get; private set; }

        internal Exception? Exception { get; init; }

        internal int Result { get; init; }

        public Task<int> DeleteCompletedOlderThanAsync(
            DateTimeOffset olderThan,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Cutoff = olderThan;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }
}
