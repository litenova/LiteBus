using LiteBus.Inbox.PostgreSql;
using LiteBus.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlInboxSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options);
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    [Fact]
    public async Task EnsureAsync_ShouldUpgradeLegacyVersion1Table()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        var legacyOptions = options with { TableName = $"{options.TableName}_legacy" };

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, legacyOptions);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var dropColumn = connection.CreateCommand();
        dropColumn.CommandText = $"""
                                  ALTER TABLE "{legacyOptions.SchemaName}"."{legacyOptions.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;
        await dropColumn.ExecuteNonQueryAsync();

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, legacyOptions);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, legacyOptions);
    }

    [Fact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options))
            .ToArray();

        await Task.WhenAll(tasks);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Inbox);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_ShouldDelegateToEnsureAsync()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        await PostgreSqlInboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }
}
