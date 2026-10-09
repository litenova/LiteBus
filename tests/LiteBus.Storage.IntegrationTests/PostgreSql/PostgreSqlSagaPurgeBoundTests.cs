using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;
using LiteBus.Testing;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL saga store rejects unbounded purges and honors the Take limit.
/// </summary>
public sealed class PostgreSqlSagaPurgeBoundTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaPurgeBoundTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PostgreSqlSagaPurgeBoundTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies a purge with no predicates and no Take limit is rejected.
    /// </summary>
    [SkippableFact]
    public async Task PurgeAsync_WithEmptyFilter_ThrowsArgumentException()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);

        var action = () => store.PurgeAsync(new SagaPurgeFilter());

        await action.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a non-positive Take limit is rejected.
    /// </summary>
    /// <param name="take">The invalid Take limit.</param>
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PurgeAsync_WithNonPositiveTake_ThrowsArgumentOutOfRange(int take)
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);

        var action = () => store.PurgeAsync(new SagaPurgeFilter { Take = take });

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a Take-only purge deletes at most Take rows, oldest first.
    /// </summary>
    [SkippableFact]
    public async Task PurgeAsync_WithTake_DeletesOldestMatchesFirst()
    {
        var clock = new ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var store = await CreateStoreAsync(clock).ConfigureAwait(false);

        for (var index = 0; index < 12; index++)
        {
            await SaveNewAsync(store, $"order-{index:00}", index).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var removed = await store.PurgeAsync(new SagaPurgeFilter { Take = 10 }).ConfigureAwait(false);
        var remaining = await store.QueryAsync(new SagaQueryFilter { Take = 20 }).ConfigureAwait(false);

        removed.Should().Be(10);
        remaining.Select(summary => summary.Correlation.CorrelationId)
            .Should()
            .BeEquivalentTo("order-10", "order-11");
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

    private static Task SaveNewAsync(PostgreSqlSagaStore store, string correlationId, int step)
    {
        return store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            CreateCorrelation(correlationId),
            new TestSagaState { Step = step },
            0));
    }

    private sealed class TestSagaState
    {
        public int Step { get; set; }
    }
}
