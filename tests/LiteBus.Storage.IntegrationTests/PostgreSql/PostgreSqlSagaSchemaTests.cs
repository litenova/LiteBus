using LiteBus.Saga.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlSagaSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSagaSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldCreateSagaTableReadableThroughTableReaders()
    {
        var options = CreateSagaStoreOptions();

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlSagaSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var rowCount = await PostgreSqlTableReaders.CountSagaRowsAsync(_fixture.DataSource, options).ConfigureAwait(false);
        rowCount.Should().Be(0);

        var missing = await PostgreSqlTableReaders.ReadSagaAsync(
            _fixture.DataSource,
            options,
            "no-such-correlation",
            "orders").ConfigureAwait(false);
        missing.Should().BeNull();
    }

    [SkippableFact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        var options = CreateSagaStoreOptions();

        var action = async () => await PostgreSqlSagaSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Saga).ConfigureAwait(false);
    }

    private static PostgreSqlSagaStoreOptions CreateSagaStoreOptions(string? tableName = null)
    {
        return new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = tableName ?? $"saga_{Guid.NewGuid():N}"
        };
    }
}
