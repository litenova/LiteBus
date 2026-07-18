using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies outbox manager pagination, diagnostics, purge, and retention behavior.
/// </summary>
public sealed class OutboxManagerOperationsTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies query filters, missing lookup behavior, status counts, and logical schema information.
    /// </summary>
    [Fact]
    public async Task BrowseAndDiagnostics_ReflectStoredRows()
    {
        var store = new InMemoryOutboxStore();
        var manager = CreateManager(store);
        var matchingId = Guid.NewGuid();

        await store.AddBatchAsync([
            CreateEnvelope(matchingId, OutboxStatus.DeadLettered, "tenant-a"),
            CreateEnvelope(Guid.NewGuid(), OutboxStatus.Pending, "tenant-b")
        ]).ConfigureAwait(false);

        var page = await manager.QueryAsync(
            new OutboxMessageFilter
            {
                Statuses = [OutboxStatus.DeadLettered],
                TenantId = "tenant-a"
            },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var missing = await manager.GetMessageAsync(Guid.NewGuid()).ConfigureAwait(false);
        var counts = await manager.GetStatusCountsAsync().ConfigureAwait(false);
        var schema = await manager.GetSchemaInfoAsync().ConfigureAwait(false);

        page.Items.Should().ContainSingle(item => item.Id == matchingId);
        missing.Should().BeNull();
        counts[OutboxStatus.DeadLettered].Should().Be(1);
        counts[OutboxStatus.Pending].Should().Be(1);
        schema.Component.Should().Be("outbox");
        schema.RecordedVersion.Should().Be(schema.ExpectedVersion);
    }

    /// <summary>
    ///     Verifies bulk dead-letter replay follows keyset pagination beyond the internal page size.
    /// </summary>
    [Fact]
    public async Task RequeueDeadLettersAsync_WhenMoreThanOnePage_RequeuesEveryRow()
    {
        var store = new InMemoryOutboxStore();
        var manager = CreateManager(store);
        var envelopes = Enumerable.Range(0, 201)
            .Select(index => CreateEnvelope(
                Guid.NewGuid(),
                OutboxStatus.DeadLettered,
                null,
                BaseTime.AddTicks(index)))
            .ToArray();

        await store.AddBatchAsync(envelopes).ConfigureAwait(false);

        var requeued = await manager.RequeueDeadLettersAsync().ConfigureAwait(false);

        requeued.Should().Be(201);
        store.GetAll(OutboxStatus.DeadLettered).Should().BeEmpty();
        store.GetAll(OutboxStatus.Pending).Should().HaveCount(201);
    }

    /// <summary>
    ///     Verifies empty replay requests do not call the store and report zero requested rows.
    /// </summary>
    [Fact]
    public async Task RequeueOperations_WhenNoRows_ReturnZeroCounts()
    {
        var store = new InMemoryOutboxStore();
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
        var store = new InMemoryOutboxStore();
        var manager = CreateManager(store);

        await store.AddBatchAsync([
            CreateEnvelope(Guid.NewGuid(), OutboxStatus.Published, "tenant-a"),
            CreateEnvelope(Guid.NewGuid(), OutboxStatus.Pending, "tenant-b")
        ]).ConfigureAwait(false);

        var narrowed = await manager.PurgeAsync(new OutboxMessageFilter { TenantId = "tenant-a" })
            .ConfigureAwait(false);
        var unrestricted = await manager.PurgeAsync(new OutboxMessageFilter(), confirm: true)
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
        var store = new InMemoryOutboxStore();
        var retentionStore = new RecordingRetentionStore();

        var missing = CreateManager(
            store,
            new OutboxCleanupHostOptions { Retention = null },
            retentionStore,
            new ManualTimeProvider(BaseTime));
        var zero = CreateManager(
            store,
            new OutboxCleanupHostOptions { Retention = TimeSpan.Zero },
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
        var store = new InMemoryOutboxStore();
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
        var store = new InMemoryOutboxStore();
        var options = new OutboxCleanupHostOptions
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
        var store = new InMemoryOutboxStore();
        var options = new OutboxCleanupHostOptions { Retention = TimeSpan.FromDays(1) };
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

    private static OutboxEnvelope CreateEnvelope(
        Guid id,
        OutboxStatus status,
        string? tenantId,
        DateTimeOffset? createdAt = null)
    {
        return new OutboxEnvelope
        {
            Id = id,
            ContractName = "tests.events.shipped",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt ?? BaseTime,
            PublishedAt = status == OutboxStatus.Published ? createdAt ?? BaseTime : null,
            Status = status,
            AttemptCount = 0,
            TenantId = tenantId
        };
    }

    private static OutboxManager CreateManager(InMemoryOutboxStore store)
    {
        var options = new OutboxCleanupHostOptions();
        return CreateManager(store, options, store, TimeProvider.System);
    }

    private static OutboxManager CreateManager(
        InMemoryOutboxStore store,
        OutboxCleanupHostOptions options,
        IOutboxRetentionStore retentionStore,
        TimeProvider timeProvider)
    {
        return new OutboxManager(
            store,
            retentionStore,
            new OutboxRetentionCoordinator(options),
            options,
            timeProvider);
    }

    private sealed class RecordingRetentionStore : IOutboxRetentionStore
    {
        internal int CallCount { get; private set; }

        internal DateTimeOffset? Cutoff { get; private set; }

        internal Exception? Exception { get; init; }

        internal int Result { get; init; }

        public Task<int> DeletePublishedOlderThanAsync(
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
