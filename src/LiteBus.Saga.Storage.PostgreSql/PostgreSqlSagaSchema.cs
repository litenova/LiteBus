using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL saga schema used by <see cref="PostgreSqlSagaStore" />.
/// </summary>
public static class PostgreSqlSagaSchema
{
    /// <summary>
    ///     Gets the saga table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Ensures the saga schema exists.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The saga store options.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when schema creation finishes.</returns>
    public static async Task EnsureAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlSagaStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        options ??= new PostgreSqlSagaStoreOptions();

        var sql = PostgreSqlSagaSchemaScripts.GetCreateScript(options);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates that the saga table exists with required columns.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The saga store options.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when validation finishes.</returns>
    public static async Task ValidateAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlSagaStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        options ??= new PostgreSqlSagaStoreOptions();

        var qualifiedTableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        var sql = $"""
                  SELECT column_name
                  FROM information_schema.columns
                  WHERE table_schema = @schema_name
                      AND table_name = @table_name;
                  """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema_name", options.SchemaName);
        command.Parameters.AddWithValue("table_name", options.TableName);

        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(0));
        }

        string[] required =
        [
            "correlation_id",
            "saga_type",
            "state_json",
            "optimistic_lock_version",
            "is_completed",
            "created_at",
            "updated_at"
        ];

        foreach (var column in required)
        {
            if (!columns.Contains(column))
            {
                throw new InvalidOperationException(
                    $"Saga table '{qualifiedTableName}' is missing required column '{column}'.");
            }
        }
    }
}
