using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

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
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
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
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
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
    public async Task InboxValidateAsync_WhenRequiredIndexMissing_ShouldThrowWithMissingIndexes()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options);

        var indexName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "idempotency_key_uidx");

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var dropIndex = connection.CreateCommand();
        dropIndex.CommandText = $"""DROP INDEX IF EXISTS "{options.SchemaName}"."{indexName}";""";
        await dropIndex.ExecuteNonQueryAsync();

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Inbox &&
                exception.Details.Contains(indexName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutboxValidateAsync_WhenRequiredIndexMissing_ShouldThrowWithMissingIndexes()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options);

        var indexName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "lease_idx");

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var dropIndex = connection.CreateCommand();
        dropIndex.CommandText = $"""DROP INDEX IF EXISTS "{options.SchemaName}"."{indexName}";""";
        await dropIndex.ExecuteNonQueryAsync();

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.Details.Contains(indexName, StringComparison.Ordinal));
    }
}