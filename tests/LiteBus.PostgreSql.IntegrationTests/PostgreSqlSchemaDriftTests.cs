using LiteBus.Inbox.PostgreSql;
using LiteBus.Outbox.PostgreSql;
using LiteBus.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlSchemaDriftTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaDriftTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var dropColumn = connection.CreateCommand();
        dropColumn.CommandText = $"""
                                  ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;
        await dropColumn.ExecuteNonQueryAsync();

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Inbox &&
                exception.Details.Contains("trace_context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var dropColumn = connection.CreateCommand();
        dropColumn.CommandText = $"""
                                  ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;
        await dropColumn.ExecuteNonQueryAsync();

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.Details.Contains("trace_context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InboxValidateAsync_WhenMetadataVersionIsStale_ShouldThrow()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var updateVersion = connection.CreateCommand();
        updateVersion.CommandText = $"""
                                     UPDATE "{options.MetadataSchemaName}"."{options.MetadataTableName}"
                                     SET version = 1
                                     WHERE component = @component
                                       AND schema_name = @schema_name
                                       AND table_name = @table_name;
                                     """;
        updateVersion.Parameters.AddWithValue("component", PostgreSqlSchemaComponents.Inbox);
        updateVersion.Parameters.AddWithValue("schema_name", options.SchemaName);
        updateVersion.Parameters.AddWithValue("table_name", options.TableName);
        await updateVersion.ExecuteNonQueryAsync();

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Inbox &&
                exception.ActualVersion == 1 &&
                exception.ExpectedVersion == PostgreSqlInboxSchema.CurrentSchemaVersion);
    }

    [Fact]
    public async Task OutboxValidateAsync_WhenMetadataVersionIsStale_ShouldThrow()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var updateVersion = connection.CreateCommand();
        updateVersion.CommandText = $"""
                                     UPDATE "{options.MetadataSchemaName}"."{options.MetadataTableName}"
                                     SET version = 1
                                     WHERE component = @component
                                       AND schema_name = @schema_name
                                       AND table_name = @table_name;
                                     """;
        updateVersion.Parameters.AddWithValue("component", PostgreSqlSchemaComponents.Outbox);
        updateVersion.Parameters.AddWithValue("schema_name", options.SchemaName);
        updateVersion.Parameters.AddWithValue("table_name", options.TableName);
        await updateVersion.ExecuteNonQueryAsync();

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.ActualVersion == 1 &&
                exception.ExpectedVersion == PostgreSqlOutboxSchema.CurrentSchemaVersion);
    }
}
