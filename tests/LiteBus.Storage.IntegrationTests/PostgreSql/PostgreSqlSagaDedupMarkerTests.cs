using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL saga store preserves the duplicate-dispatch marker when a save carries no identifier.
/// </summary>
public sealed class PostgreSqlSagaDedupMarkerTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaDedupMarkerTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PostgreSqlSagaDedupMarkerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies a save without an applied message identifier keeps the stored marker.
    /// </summary>
    [SkippableFact]
    public async Task SaveAsync_WithoutAppliedMessageId_PreservesStoredMarker()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var correlation = CreateCorrelation("dedup-marker");
        var firstMessageId = Guid.NewGuid();

        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 1 },
            0,
            firstMessageId)).ConfigureAwait(false);
        await store.SaveAsync(SagaSaveItem<TestSagaState>.From(
            correlation,
            new TestSagaState { Step = 2 },
            1)).ConfigureAwait(false);

        var loaded = await store.LoadAsync<TestSagaState>(correlation).ConfigureAwait(false);

        loaded.Should().NotBeNull();
        loaded!.State.Step.Should().Be(2);
        loaded.Version.Should().Be(2);
        loaded.LastAppliedMessageId.Should().Be(firstMessageId);
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

    private sealed class TestSagaState
    {
        public int Step { get; set; }
    }
}
