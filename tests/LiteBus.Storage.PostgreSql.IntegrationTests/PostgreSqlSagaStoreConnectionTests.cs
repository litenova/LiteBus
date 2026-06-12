using LiteBus.Messaging;
using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for <see cref="PostgreSqlSagaStore" /> connection ownership under load.
/// </summary>
public sealed class PostgreSqlSagaStoreConnectionTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSagaStoreConnectionTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms repeated save/load cycles do not exhaust the PostgreSQL connection pool.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_under_load_should_not_exhaust_connection_pool()
    {
        var options = new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, options);

        var limitedConnectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            MaxPoolSize = 4
        }.ConnectionString;

        await using var limitedDataSource = NpgsqlDataSource.Create(limitedConnectionString);

        var store = new PostgreSqlSagaStore(
            limitedDataSource,
            new SystemTextJsonMessageSerializer(),
            options);

        var correlation = new SagaCorrelation
        {
            SagaDefinitionId = "order-flow",
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        for (var iteration = 0; iteration < 40; iteration++)
        {
            var instance = await store.LoadAsync<TestSagaState>(correlation);
            var version = instance?.Version ?? 0;
            var state = instance?.State ?? new TestSagaState();

            state.Counter = iteration + 1;
            await store.SaveAsync(new SagaSaveItem<TestSagaState>(correlation, state, version));
        }

        var loaded = await store.LoadAsync<TestSagaState>(correlation);
        loaded.Should().NotBeNull();
        loaded!.State.Counter.Should().Be(40);
    }

    /// <summary>
    ///     A minimal saga state payload used by connection ownership tests.
    /// </summary>
    private sealed class TestSagaState
    {
        /// <summary>
        ///     Gets or sets the iteration counter stored by the saga.
        /// </summary>
        public int Counter { get; set; }
    }
}