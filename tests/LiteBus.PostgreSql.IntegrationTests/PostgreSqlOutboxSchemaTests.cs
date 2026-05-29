using LiteBus.Outbox.PostgreSql;
using LiteBus.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlOutboxSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options);
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options);

        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    [Fact]
    public async Task EnsureAsync_ShouldUpgradeLegacyVersion1Table()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        var legacyOptions = options with { TableName = $"{options.TableName}_legacy" };

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, legacyOptions);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var dropColumn = connection.CreateCommand();
        dropColumn.CommandText = $"""
                                  ALTER TABLE "{legacyOptions.SchemaName}"."{legacyOptions.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;
        await dropColumn.ExecuteNonQueryAsync();

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, legacyOptions);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, legacyOptions);
    }

    [Fact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options))
            .ToArray();

        await Task.WhenAll(tasks);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Outbox);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_ShouldDelegateToEnsureAsync()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        await PostgreSqlOutboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
    }
}
