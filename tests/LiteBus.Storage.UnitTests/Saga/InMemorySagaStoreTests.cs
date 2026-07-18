using LiteBus.Messaging;
using LiteBus.Saga;
using LiteBus.Saga.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies in-memory saga persistence, optimistic concurrency, filtering, and retention semantics.
/// </summary>
public sealed class InMemorySagaStoreTests
{
    /// <summary>
    ///     The base timestamp used for deterministic saga row timestamps.
    /// </summary>
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies inserts and updates preserve creation time while advancing state, version, and update time.
    /// </summary>
    [Fact]
    public async Task SaveAsync_InsertAndUpdate_ShouldPersistVersionedStateAndTimestamps()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = CreateStore(clock);
        var correlation = CreateCorrelation("order-1");

        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 1 },
            0)).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromMinutes(5));

        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 2 },
            1)).ConfigureAwait(false);

        var loaded = await store.LoadAsync<TestSagaState>(correlation).ConfigureAwait(false);
        var summaries = await store.QueryAsync(new SagaQueryFilter()).ConfigureAwait(false);

        loaded.Should().NotBeNull();
        loaded!.State.Step.Should().Be(2);
        loaded.Version.Should().Be(2);
        loaded.IsCompleted.Should().BeFalse();
        summaries.Should().ContainSingle();
        summaries[0].CreatedAt.Should().Be(BaseTime);
        summaries[0].UpdatedAt.Should().Be(BaseTime.AddMinutes(5));
    }

    /// <summary>
    ///     Verifies a missing row cannot be inserted with a stale nonzero expected version.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MissingRowWithNonzeroVersion_ShouldThrowConcurrencyException()
    {
        var store = CreateStore();
        var correlation = CreateCorrelation("order-2");

        var action = () => store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 1 },
            3));

        await action.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);
        (await store.LoadAsync<TestSagaState>(correlation).ConfigureAwait(false)).Should().BeNull();
    }

    /// <summary>
    ///     Verifies stale saves and saves against completed rows cannot overwrite stored state.
    /// </summary>
    [Fact]
    public async Task SaveAsync_StaleOrCompletedRow_ShouldThrowConcurrencyException()
    {
        var store = CreateStore();
        var correlation = CreateCorrelation("order-3");

        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 1 },
            0)).ConfigureAwait(false);

        var staleSave = () => store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 2 },
            0));

        await staleSave.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);

        await store.CompleteAsync(SagaCompleteItem.From(correlation, 1)).ConfigureAwait(false);

        var completedSave = () => store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 3 },
            2));

        await completedSave.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);

        var loaded = await store.LoadAsync<TestSagaState>(correlation).ConfigureAwait(false);
        loaded!.State.Step.Should().Be(1);
        loaded.Version.Should().Be(2);
        loaded.IsCompleted.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies completion rejects missing, stale, and already completed saga rows.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_WithInvalidVersionOrState_ShouldThrowConcurrencyException()
    {
        var store = CreateStore();
        var correlation = CreateCorrelation("order-4");

        var missing = () => store.CompleteAsync(SagaCompleteItem.From(correlation, 1));
        await missing.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);

        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState(),
            0)).ConfigureAwait(false);

        var stale = () => store.CompleteAsync(SagaCompleteItem.From(correlation, 2));
        await stale.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);

        await store.CompleteAsync(SagaCompleteItem.From(correlation, 1)).ConfigureAwait(false);

        var repeated = () => store.CompleteAsync(SagaCompleteItem.From(correlation, 2));
        await repeated.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies two workers using the same expected version cannot both complete one saga row.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ConcurrentWorkers_ShouldAllowOneCompletion()
    {
        var store = CreateStore();
        var correlation = CreateCorrelation("order-5");
        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState(),
            0)).ConfigureAwait(false);

        var attempts = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await store.CompleteAsync(SagaCompleteItem.From(correlation, 1)).ConfigureAwait(false);
                    return true;
                }
                catch (SagaConcurrencyException)
                {
                    return false;
                }
            }))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts).ConfigureAwait(false);
        var loaded = await store.LoadAsync<TestSagaState>(correlation).ConfigureAwait(false);

        outcomes.Count(success => success).Should().Be(1);
        loaded!.Version.Should().Be(2);
        loaded.IsCompleted.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies identifier delimiters cannot alias distinct saga correlations to one in-memory row.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithColonDelimitedIdentifiers_ShouldKeepDistinctRows()
    {
        var store = CreateStore();
        var first = CreateCorrelation("42", "orders", "tenant:blue");
        var second = CreateCorrelation("42", "blue:orders", "tenant");

        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            first,
            new TestSagaState { Step = 1 },
            0)).ConfigureAwait(false);
        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            second,
            new TestSagaState { Step = 2 },
            0)).ConfigureAwait(false);

        var firstLoaded = await store.LoadAsync<TestSagaState>(first).ConfigureAwait(false);
        var secondLoaded = await store.LoadAsync<TestSagaState>(second).ConfigureAwait(false);
        var summaries = await store.QueryAsync(new SagaQueryFilter()).ConfigureAwait(false);

        firstLoaded!.State.Step.Should().Be(1);
        secondLoaded!.State.Step.Should().Be(2);
        summaries.Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies query predicates, completion state, ordering, and take limits match the store contract.
    /// </summary>
    [Fact]
    public async Task QueryAsync_WithFilters_ShouldReturnNewestMatchingRows()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = CreateStore(clock);
        var first = CreateCorrelation("first", tenantId: "tenant-a");
        var second = CreateCorrelation("second", tenantId: "tenant-a");
        var otherTenant = CreateCorrelation("third", tenantId: "tenant-b");

        await SaveNewAsync(store, first, 1).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SaveNewAsync(store, second, 2).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(second, 1)).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SaveNewAsync(store, otherTenant, 3).ConfigureAwait(false);

        var results = await store.QueryAsync(new SagaQueryFilter
        {
            TenantId = "tenant-a",
            SagaDefinitionId = "orders",
            IsCompleted = true,
            Take = 1
        }).ConfigureAwait(false);

        results.Should().ContainSingle();
        results[0].Correlation.Should().Be(second);
        results[0].Version.Should().Be(2);
        results[0].IsCompleted.Should().BeTrue();

        var invalidTake = () => store.QueryAsync(new SagaQueryFilter { Take = 0 });
        await invalidTake.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies completed-before purge uses the completion timestamp and preserves newer or active rows.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithCompletedBefore_ShouldRemoveOnlyOlderCompletedRows()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = CreateStore(clock);
        var oldCompleted = CreateCorrelation("old");
        var newCompleted = CreateCorrelation("new");
        var active = CreateCorrelation("active");

        await SaveNewAsync(store, oldCompleted, 1).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(oldCompleted, 1)).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromHours(2));
        var cutoff = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));

        await SaveNewAsync(store, newCompleted, 2).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(newCompleted, 1)).ConfigureAwait(false);
        await SaveNewAsync(store, active, 3).ConfigureAwait(false);

        var removed = await store.PurgeAsync(new SagaPurgeFilter
        {
            CompletedBefore = cutoff
        }).ConfigureAwait(false);
        var remaining = await store.QueryAsync(new SagaQueryFilter()).ConfigureAwait(false);

        removed.Should().Be(1);
        remaining.Select(summary => summary.Correlation).Should().BeEquivalentTo([newCompleted, active]);
    }

    private static InMemorySagaStore CreateStore(TimeProvider? clock = null)
    {
        return new InMemorySagaStore(new SystemTextJsonMessageSerializer(), clock);
    }

    private static SagaCorrelation CreateCorrelation(
        string correlationId,
        string sagaDefinitionId = "orders",
        string? tenantId = null)
    {
        return new SagaCorrelation
        {
            CorrelationId = correlationId,
            SagaDefinitionId = sagaDefinitionId,
            TenantId = tenantId
        };
    }

    private static Task SaveNewAsync(InMemorySagaStore store, SagaCorrelation correlation, int step)
    {
        return store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = step },
            0));
    }

    private sealed class TestSagaState
    {
        public int Step { get; set; }
    }
}
