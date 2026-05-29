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
    public void GetCreateScript_ShouldIncludeCurrentVersionObjects()
    {
        var options = CreateOptions();
        var script = PostgreSqlOutboxSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain("trace_context");
        script.Should().Contain(options.MetadataTableName);
    }

    [Fact]
    public void GetUpgradeScript_ShouldReturnVersion2Changes()
    {
        var options = CreateOptions();
        var script = PostgreSqlOutboxSchema.GetUpgradeScript(1, 2, options);

        script.Should().Contain("trace_context");
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource!, options);
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource!, options);

        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource!, options);
    }

    [Fact]
    public async Task EnsureAsync_ShouldUpgradeLegacyVersion1Table()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();
        var legacyOptions = options with { TableName = $"{options.TableName}_legacy" };

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource!, legacyOptions);

        await using var connection = await _fixture.DataSource!.OpenConnectionAsync();
        await using var dropColumn = connection.CreateCommand();
        dropColumn.CommandText = $"""
                                  ALTER TABLE "{legacyOptions.SchemaName}"."{legacyOptions.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;
        await dropColumn.ExecuteNonQueryAsync();

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource!, legacyOptions);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource!, legacyOptions);
    }

    [Fact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource!, options))
            .ToArray();

        await Task.WhenAll(tasks);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource!, options);
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        _fixture.RequireDocker();

        var options = CreateOptions();

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource!, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Outbox);
    }

    private static PostgreSqlOutboxStoreOptions CreateOptions()
    {
        return new PostgreSqlOutboxStoreOptions
        {
            SchemaName = "litebus_tests",
            TableName = $"outbox_schema_{Guid.NewGuid():N}"
        };
    }
}
