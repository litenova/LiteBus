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
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(true);

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var dropColumn = connection.CreateCommand();
         await using (dropColumn.ConfigureAwait(false))
         {

        dropColumn.CommandText = $"""
                                  ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;

        await dropColumn.ExecuteNonQueryAsync().ConfigureAwait(true);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(true);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Inbox &&
                exception.Details.Contains("trace_context", StringComparison.Ordinal));
        }
        }
    }

    [Fact]
    public async Task OutboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(true);

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var dropColumn = connection.CreateCommand();
         await using (dropColumn.ConfigureAwait(false))
         {

        dropColumn.CommandText = $"""
                                  ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;

        await dropColumn.ExecuteNonQueryAsync().ConfigureAwait(true);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(true);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.Details.Contains("trace_context", StringComparison.Ordinal));
        }
        }
    }

    [Fact]
    public async Task InboxValidateAsync_WhenRequiredIndexMissing_ShouldThrowWithMissingIndexes()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(true);

        var indexName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "idempotency_key_uidx");

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var dropIndex = connection.CreateCommand();
         await using (dropIndex.ConfigureAwait(false))
         {
        dropIndex.CommandText = $"""DROP INDEX IF EXISTS "{options.SchemaName}"."{indexName}";""";
        await dropIndex.ExecuteNonQueryAsync().ConfigureAwait(true);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Inbox &&
                exception.Details.Contains(indexName, StringComparison.Ordinal));
        }
        }
    }

    [Fact]
    public async Task OutboxValidateAsync_WhenRequiredIndexMissing_ShouldThrowWithMissingIndexes()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(true);

        var indexName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "lease_idx");

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var dropIndex = connection.CreateCommand();
         await using (dropIndex.ConfigureAwait(false))
         {
        dropIndex.CommandText = $"""DROP INDEX IF EXISTS "{options.SchemaName}"."{indexName}";""";
        await dropIndex.ExecuteNonQueryAsync().ConfigureAwait(false);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.Details.Contains(indexName, StringComparison.Ordinal));
        }
        }
    }
}
