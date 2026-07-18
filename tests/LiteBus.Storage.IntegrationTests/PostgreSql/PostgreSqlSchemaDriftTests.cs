using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

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
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var dropColumn = connection.CreateCommand();
            await using (dropColumn.ConfigureAwait(false))
            {
                dropColumn.CommandText = $"""
                                  ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;

                await dropColumn.ExecuteNonQueryAsync().ConfigureAwait(false);

                var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

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
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var dropColumn = connection.CreateCommand();
            await using (dropColumn.ConfigureAwait(false))
            {
                dropColumn.CommandText = $"""
                                  ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                      DROP COLUMN IF EXISTS trace_context;
                                  """;

                await dropColumn.ExecuteNonQueryAsync().ConfigureAwait(false);

                var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

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
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var indexName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "idempotency_key_uidx");

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var dropIndex = connection.CreateCommand();
            await using (dropIndex.ConfigureAwait(false))
            {
                dropIndex.CommandText = $"""DROP INDEX IF EXISTS "{options.SchemaName}"."{indexName}";""";
                await dropIndex.ExecuteNonQueryAsync().ConfigureAwait(false);

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
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var indexName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "lease_idx");

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
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

    [Fact]
    public async Task OutboxEnsureAsync_WhenV5TableExists_ShouldRejectAutomaticMigration()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = $"""
                                      CREATE SCHEMA IF NOT EXISTS "{options.SchemaName}";

                                      CREATE TABLE "{options.SchemaName}"."{options.TableName}"
                                      (
                                          message_id uuid PRIMARY KEY,
                                          contract_name text NOT NULL,
                                          contract_version integer NOT NULL,
                                          payload jsonb NOT NULL,
                                          topic text NULL,
                                          created_at timestamptz NOT NULL,
                                          visible_after timestamptz NULL,
                                          status integer NOT NULL,
                                          attempt_count integer NOT NULL,
                                          lease_owner text NULL,
                                          lease_expires_at timestamptz NULL,
                                          last_error text NULL,
                                          correlation_id text NULL,
                                          causation_id text NULL,
                                          tenant_id text NULL,
                                          trace_context jsonb NULL
                                      );
                                      """;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        var action = async () =>
            await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.ExpectedVersion == 1 &&
                exception.Details.Contains("does not mutate v5 tables", StringComparison.Ordinal))
            .ConfigureAwait(false);
    }
}
