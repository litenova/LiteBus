using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Creates the PostgreSQL command inbox schema objects used by <see cref="PostgreSqlCommandInboxStore" />.
/// </summary>
/// <remarks>
///     Applications can call this helper during startup, migrations, or tests. Larger systems may copy the generated
///     table and index shape into their own migration tool instead. The schema uses `jsonb` payloads, a unique partial
///     idempotency index, and a leasing index tuned for pending, failed, and expired processing rows.
/// </remarks>
public static class PostgreSqlInboxSchema
{
    /// <summary>
    ///     Creates the command inbox table and indexes when they do not exist.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options. Defaults create `public.litebus_inbox_commands`.</param>
    /// <param name="cancellationToken">A token used to cancel the database command before it completes.</param>
    public static async Task CreateIfNotExistsAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlInboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlInboxStoreOptions();

        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        var schemaName = PostgreSqlIdentifier.Quote(options.SchemaName);
        var idempotencyIndexName = PostgreSqlIdentifier.IndexName(options.TableName, "idempotency_key_uidx");
        var leasingIndexName = PostgreSqlIdentifier.IndexName(options.TableName, "lease_idx");

        var sql = $"""
                  CREATE SCHEMA IF NOT EXISTS {schemaName};

                  CREATE TABLE IF NOT EXISTS {tableName} (
                      command_id uuid PRIMARY KEY,
                      contract_name text NOT NULL,
                      contract_version integer NOT NULL,
                      payload jsonb NOT NULL,
                      created_at timestamptz NOT NULL,
                      visible_after timestamptz NULL,
                      attempt_count integer NOT NULL,
                      status integer NOT NULL,
                      idempotency_key text NULL,
                      lease_owner text NULL,
                      lease_expires_at timestamptz NULL,
                      last_error text NULL,
                      correlation_id text NULL,
                      causation_id text NULL,
                      tenant_id text NULL
                  );

                  CREATE UNIQUE INDEX IF NOT EXISTS {idempotencyIndexName}
                      ON {tableName} (idempotency_key)
                      WHERE idempotency_key IS NOT NULL;

                  CREATE INDEX IF NOT EXISTS {leasingIndexName}
                      ON {tableName} (status, visible_after, lease_expires_at, created_at);
                  """;

        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}