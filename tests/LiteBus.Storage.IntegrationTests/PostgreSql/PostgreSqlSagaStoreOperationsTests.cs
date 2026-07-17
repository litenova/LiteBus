using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;
using LiteBus.Testing;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies PostgreSQL saga persistence, tenant isolation, filtering, retention, and optimistic concurrency.
/// </summary>
public sealed class PostgreSqlSagaStoreOperationsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaStoreOperationsTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PostgreSqlSagaStoreOperationsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies the tenant identifier participates in the primary key and tenant filters select the matching row.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SameCorrelationAcrossTenants_ShouldKeepRowsIsolated()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var tenantA = CreateCorrelation("order-42", tenantId: "tenant-a");
        var tenantB = CreateCorrelation("order-42", tenantId: "tenant-b");

        await SaveNewAsync(store, tenantA, 1).ConfigureAwait(false);
        await SaveNewAsync(store, tenantB, 2).ConfigureAwait(false);

        var loadedA = await store.LoadAsync<TestSagaState>(tenantA).ConfigureAwait(false);
        var loadedB = await store.LoadAsync<TestSagaState>(tenantB).ConfigureAwait(false);
        var allTenants = await store.QueryAsync(new SagaQueryFilter
        {
            SagaDefinitionId = "orders",
            CorrelationId = "order-42"
        }).ConfigureAwait(false);
        var tenantAOnly = await store.QueryAsync(new SagaQueryFilter
        {
            TenantId = "tenant-a"
        }).ConfigureAwait(false);

        loadedA!.State.Step.Should().Be(1);
        loadedB!.State.Step.Should().Be(2);
        allTenants.Select(summary => summary.Correlation).Should().BeEquivalentTo([tenantA, tenantB]);
        tenantAOnly.Should().ContainSingle();
        tenantAOnly[0].Correlation.Should().Be(tenantA);
    }

    /// <summary>
    ///     Verifies query predicates, descending update order, take limits, and invalid take validation.
    /// </summary>
    [Fact]
    public async Task QueryAsync_WithFilters_ShouldReturnNewestMatchingRows()
    {
        var clock = new ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var store = await CreateStoreAsync(clock).ConfigureAwait(false);
        var older = CreateCorrelation("older", tenantId: "tenant-a");
        var completed = CreateCorrelation("completed", tenantId: "tenant-a");
        var otherTenant = CreateCorrelation("completed", tenantId: "tenant-b");
        var otherDefinition = CreateCorrelation("completed", "payments", "tenant-a");

        await SaveNewAsync(store, older, 1).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SaveNewAsync(store, completed, 2).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(completed, 1)).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SaveNewAsync(store, otherTenant, 3).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SaveNewAsync(store, otherDefinition, 4).ConfigureAwait(false);

        var all = await store.QueryAsync(new SagaQueryFilter { Take = 10 }).ConfigureAwait(false);
        var newestTenantOrder = await store.QueryAsync(new SagaQueryFilter
        {
            SagaDefinitionId = "orders",
            TenantId = "tenant-a",
            Take = 1
        }).ConfigureAwait(false);
        var exactCompleted = await store.QueryAsync(new SagaQueryFilter
        {
            SagaDefinitionId = "orders",
            CorrelationId = "completed",
            TenantId = "tenant-a",
            IsCompleted = true
        }).ConfigureAwait(false);

        all.Should().HaveCount(4);
        all.Select(summary => summary.Correlation).Should().ContainInOrder(
            otherDefinition,
            otherTenant,
            completed,
            older);
        newestTenantOrder.Should().ContainSingle();
        newestTenantOrder[0].Correlation.Should().Be(completed);
        exactCompleted.Should().ContainSingle();
        exactCompleted[0].Version.Should().Be(2);
        exactCompleted[0].IsCompleted.Should().BeTrue();

        var invalidTake = () => store.QueryAsync(new SagaQueryFilter { Take = 0 });
        await invalidTake.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies completed-before retention respects tenant filters and never removes active saga rows.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithCompletedBefore_ShouldRemoveOnlyOlderCompletedRows()
    {
        var clock = new ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var store = await CreateStoreAsync(clock).ConfigureAwait(false);
        var oldTenantA = CreateCorrelation("old-a", tenantId: "tenant-a");
        var oldTenantB = CreateCorrelation("old-b", tenantId: "tenant-b");
        var newTenantA = CreateCorrelation("new-a", tenantId: "tenant-a");
        var activeTenantA = CreateCorrelation("active-a", tenantId: "tenant-a");

        await SaveNewAsync(store, oldTenantA, 1).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(oldTenantA, 1)).ConfigureAwait(false);
        await SaveNewAsync(store, oldTenantB, 2).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(oldTenantB, 1)).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromHours(2));
        var cutoff = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));

        await SaveNewAsync(store, newTenantA, 3).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(newTenantA, 1)).ConfigureAwait(false);
        await SaveNewAsync(store, activeTenantA, 4).ConfigureAwait(false);

        var tenantRemoved = await store.PurgeAsync(new SagaPurgeFilter
        {
            TenantId = "tenant-a",
            CompletedBefore = cutoff
        }).ConfigureAwait(false);
        var allTenantRemoved = await store.PurgeAsync(new SagaPurgeFilter
        {
            CompletedBefore = cutoff
        }).ConfigureAwait(false);
        var remaining = await store.QueryAsync(new SagaQueryFilter { Take = 10 }).ConfigureAwait(false);

        tenantRemoved.Should().Be(1);
        allTenantRemoved.Should().Be(1);
        remaining.Select(summary => summary.Correlation).Should().BeEquivalentTo([newTenantA, activeTenantA]);
        remaining.Should().Contain(summary => !summary.IsCompleted && summary.Correlation == activeTenantA);
    }

    /// <summary>
    ///     Verifies two workers cannot complete the same saga version successfully.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ConcurrentWorkers_ShouldAllowOneCompletion()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var correlation = CreateCorrelation("completion-race", tenantId: "tenant-a");
        await SaveNewAsync(store, correlation, 1).ConfigureAwait(false);

        var outcomes = await Task.WhenAll(
            TryCompleteAsync(store, correlation),
            TryCompleteAsync(store, correlation)).ConfigureAwait(false);
        var loaded = await store.LoadAsync<TestSagaState>(correlation).ConfigureAwait(false);

        outcomes.Count(completed => completed).Should().Be(1);
        loaded!.Version.Should().Be(2);
        loaded.IsCompleted.Should().BeTrue();
    }

    private async Task<PostgreSqlSagaStore> CreateStoreAsync(TimeProvider? clock = null)
    {
        var options = new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        return new PostgreSqlSagaStore(
            _fixture.DataSource,
            new SystemTextJsonMessageSerializer(),
            options,
            clock);
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

    private static Task SaveNewAsync(PostgreSqlSagaStore store, SagaCorrelation correlation, int step)
    {
        return store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = step },
            0));
    }

    private static async Task<bool> TryCompleteAsync(PostgreSqlSagaStore store, SagaCorrelation correlation)
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
    }

    private sealed class TestSagaState
    {
        public int Step { get; set; }
    }
}
