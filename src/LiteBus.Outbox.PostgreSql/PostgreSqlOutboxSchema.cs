using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Creates the PostgreSQL outbox schema objects used by <see cref="PostgreSqlOutboxStore" />.
/// </summary>
/// <remarks>
///     Applications can call this helper during startup, migrations, or tests. Larger systems may copy the generated
///     table and index shape into their own migration tool instead. The schema uses `jsonb` payloads, a leasing index
///     for pending, failed, and expired publishing rows, and a partial topic index for dispatcher queries.
/// </remarks>
public static class PostgreSqlOutboxSchema
{
    /// <summary>
    ///     Creates the outbox table and indexes when they do not exist.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options. Defaults create `public.litebus_outbox_messages`.</param>
    /// <param name="cancellationToken">A token used to cancel the database command before it completes.</param>
    public static async Task CreateIfNotExistsAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlOutboxStoreOptions();

        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        var schemaName = PostgreSqlIdentifier.Quote(options.SchemaName);
        var leasingIndexName = PostgreSqlIdentifier.IndexName(options.TableName, "lease_idx");
        var topicIndexName = PostgreSqlIdentifier.IndexName(options.TableName, "topic_idx");

        var sql = $"""
                  CREATE SCHEMA IF NOT EXISTS {schemaName};

                  CREATE TABLE IF NOT EXISTS {tableName} (
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
                      tenant_id text NULL
                  );

                  CREATE INDEX IF NOT EXISTS {leasingIndexName}
                      ON {tableName} (status, visible_after, lease_expires_at, created_at);

                  CREATE INDEX IF NOT EXISTS {topicIndexName}
                      ON {tableName} (topic)
                      WHERE topic IS NOT NULL;
                  """;

        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}