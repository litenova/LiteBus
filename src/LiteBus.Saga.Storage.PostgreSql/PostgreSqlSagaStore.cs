using LiteBus.Messaging.Abstractions;
using LiteBus.Saga.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     PostgreSQL-backed saga store with optimistic concurrency on <c>optimistic_lock_version</c>.
/// </summary>
public sealed class PostgreSqlSagaStore : ISagaStore
{
    /// <summary>
    ///     The time provider used to stamp create and update timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The PostgreSQL data source used to open commands against the saga table.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The serializer used to convert saga state objects to JSON.
    /// </summary>
    private readonly IMessageSerializer _serializer;

    /// <summary>
    ///     The quoted qualified saga table name built from store options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaStore" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="serializer">The serializer used to convert saga state objects to JSON.</param>
    /// <param name="options">The saga store options.</param>
    /// <param name="clock">The time provider used to stamp create and update timestamps.</param>
    public PostgreSqlSagaStore(
        NpgsqlDataSource dataSource,
        IMessageSerializer serializer,
        PostgreSqlSagaStoreOptions? options = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(serializer);

        options ??= new PostgreSqlSagaStoreOptions();
        _dataSource = dataSource;
        _serializer = serializer;
        _clock = clock ?? TimeProvider.System;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
    }

    /// <inheritdoc />
    public async Task<SagaInstance<TState>?> LoadAsync<TState>(
        SagaCorrelation correlation,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(correlation);

        var sql = $"""
                   SELECT state_json::text, optimistic_lock_version, is_completed
                   FROM {_tableName}
                   WHERE correlation_id = @correlation_id
                       AND saga_type = @saga_type
                   LIMIT 1;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("correlation_id", correlation.CorrelationId);
        command.Parameters.AddWithValue("saga_type", correlation.SagaType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var stateJson = reader.GetString(0);
        var version = reader.GetInt32(1);
        var isCompleted = reader.GetBoolean(2);
        var state = await _serializer.DeserializeAsync(typeof(TState), stateJson, cancellationToken).ConfigureAwait(false);

        return new SagaInstance<TState>
        {
            Correlation = correlation,
            State = (TState) state,
            Version = version,
            IsCompleted = isCompleted
        };
    }

    /// <inheritdoc />
    public async Task SaveAsync<TState>(
        SagaSaveItem<TState> item,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.State);

        var now = _clock.GetUtcNow();
        var stateJson = await _serializer.SerializeAsync(item.State, cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (item.ExpectedVersion == 0)
        {
            var insertSql = $"""
                             INSERT INTO {_tableName} (
                                 correlation_id,
                                 saga_type,
                                 state_json,
                                 optimistic_lock_version,
                                 is_completed,
                                 created_at,
                                 updated_at)
                             VALUES (
                                 @correlation_id,
                                 @saga_type,
                                 @state_json,
                                 1,
                                 false,
                                 @now,
                                 @now)
                             ON CONFLICT (correlation_id, saga_type) DO NOTHING;
                             """;

            await using var insertCommand = CreateCommand(connection, insertSql);
            AddCorrelationParameters(insertCommand, item.Correlation, stateJson, now);
            var inserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (inserted == 0)
            {
                throw new SagaConcurrencyException(item.Correlation);
            }

            return;
        }

        var updateSql = $"""
                         UPDATE {_tableName}
                         SET
                             state_json = @state_json,
                             optimistic_lock_version = optimistic_lock_version + 1,
                             updated_at = @now
                         WHERE correlation_id = @correlation_id
                             AND saga_type = @saga_type
                             AND optimistic_lock_version = @expected_version
                             AND is_completed = false;
                         """;

        await using var updateCommand = CreateCommand(connection, updateSql);
        AddCorrelationParameters(updateCommand, item.Correlation, stateJson, now);
        updateCommand.Parameters.AddWithValue("expected_version", item.ExpectedVersion);

        var updated = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (updated == 0)
        {
            throw new SagaConcurrencyException(item.Correlation);
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(SagaCorrelation correlation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        var now = _clock.GetUtcNow();

        var sql = $"""
                   UPDATE {_tableName}
                   SET
                       is_completed = true,
                       updated_at = @now
                   WHERE correlation_id = @correlation_id
                       AND saga_type = @saga_type;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("correlation_id", correlation.CorrelationId);
        command.Parameters.AddWithValue("saga_type", correlation.SagaType);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Opens one PostgreSQL connection for a single store operation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The open connection disposed by the caller.</returns>
    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a command bound to a caller-owned connection.
    /// </summary>
    /// <param name="connection">The open connection that owns the command lifetime.</param>
    /// <param name="sql">The SQL text.</param>
    /// <returns>The initialized command.</returns>
    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql)
    {
        return new NpgsqlCommand(sql, connection);
    }

    /// <summary>
    ///     Adds correlation, state, and timestamp parameters to one command.
    /// </summary>
    /// <param name="command">The command receiving parameters.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="stateJson">The serialized state JSON.</param>
    /// <param name="now">The current timestamp.</param>
    private static void AddCorrelationParameters(
        NpgsqlCommand command,
        SagaCorrelation correlation,
        string stateJson,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("correlation_id", correlation.CorrelationId);
        command.Parameters.AddWithValue("saga_type", correlation.SagaType);

        var stateParameter = command.Parameters.Add("state_json", NpgsqlDbType.Jsonb);
        stateParameter.Value = stateJson;
        command.Parameters.AddWithValue("now", now);
    }
}