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
    public void GetCreateScript_ShouldIncludeCurrentVersionObjects()
    {
        var options = CreateOptions();
        var script = PostgreSqlInboxSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain("trace_context");
        script.Should().Contain(options.MetadataTableName);
    }

    [Fact]
    public void GetUpgradeScript_ShouldReturnVersion2Changes()
    {
        var options = CreateOptions();
        var script = PostgreSqlInboxSchema.GetUpgradeScript(1, 2, options);

        script.Should().Contain("trace_context");
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource!, options);
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource!, options);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource!, options);
    }

    [Fact]
    public async Task EnsureAsync_ShouldUpgradeLegacyVersion1Table()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();
        var legacyOptions = options with { TableName = $"{options.TableName}_legacy" };

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource!, legacyOptions);

        await using var connection = await _fixture.DataSource!.OpenConnectionAsync();
        await using var dropColumn = connection.CreateCommand();
        dropColumn.CommandText = $"""
                                  ALTER TABLE "{legacyOptions.SchemaName}"."{legacyOptions.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;
        await dropColumn.ExecuteNonQueryAsync();

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource!, legacyOptions);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource!, legacyOptions);
    }

    [Fact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource!, options))
            .ToArray();

        await Task.WhenAll(tasks);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource!, options);
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource!, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Inbox);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_ShouldDelegateToEnsureAsync()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        await PostgreSqlInboxSchema.CreateIfNotExistsAsync(_fixture.DataSource!, options);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource!, options);
    }

    private static PostgreSqlInboxStoreOptions CreateOptions()
    {
        return new PostgreSqlInboxStoreOptions
        {
            SchemaName = "litebus_tests",
            TableName = $"inbox_schema_{Guid.NewGuid():N}"
        };
    }
}
