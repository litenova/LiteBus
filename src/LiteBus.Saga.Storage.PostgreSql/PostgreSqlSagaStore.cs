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
                   SELECT state_json::text, optimistic_lock_version, is_completed, last_applied_message_id
                   FROM {_tableName}
                   WHERE correlation_id = @correlation_id
                       AND saga_type = @saga_type
                       AND tenant_id = @tenant_id
                   LIMIT 1;
                   """;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(connection, sql);
        AddCorrelationKeyParameters(command, correlation);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var stateJson = reader.GetString(0);
        var version = reader.GetInt32(1);
        var isCompleted = reader.GetBoolean(2);
        Guid? lastAppliedMessageId = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        var state = await _serializer.DeserializeAsync(typeof(TState), stateJson, cancellationToken).ConfigureAwait(false);

        return new SagaInstance<TState>
        {
            Correlation = correlation,
            State = (TState) state,
            Version = version,
            IsCompleted = isCompleted,
            LastAppliedMessageId = lastAppliedMessageId
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
        ArgumentOutOfRangeException.ThrowIfNegative(item.ExpectedVersion);

        var now = _clock.GetUtcNow();
        var stateJson = await _serializer.SerializeAsync(item.State, cancellationToken).ConfigureAwait(false);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (item.ExpectedVersion == 0)
        {
            var insertSql = $"""
                             INSERT INTO {_tableName} (
                                 correlation_id,
                                 saga_type,
                                 tenant_id,
                                 state_json,
                                 optimistic_lock_version,
                                 is_completed,
                                 last_applied_message_id,
                                 created_at,
                                 updated_at)
                             VALUES (
                                 @correlation_id,
                                 @saga_type,
                                 @tenant_id,
                                 @state_json,
                                 1,
                                 false,
                                 @applied_message_id,
                                 @now,
                                 @now)
                             ON CONFLICT (correlation_id, saga_type, tenant_id) DO NOTHING;
                             """;

            using var insertCommand = CreateCommand(connection, insertSql);
            AddCorrelationParameters(insertCommand, item.Correlation, stateJson, now, item.AppliedMessageId);
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
                             last_applied_message_id = @applied_message_id,
                             updated_at = @now
                         WHERE correlation_id = @correlation_id
                             AND saga_type = @saga_type
                             AND tenant_id = @tenant_id
                             AND optimistic_lock_version = @expected_version
                             AND is_completed = false;
                         """;

        using var updateCommand = CreateCommand(connection, updateSql);
        AddCorrelationParameters(updateCommand, item.Correlation, stateJson, now, item.AppliedMessageId);
        updateCommand.Parameters.AddWithValue("expected_version", item.ExpectedVersion);

        var updated = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (updated == 0)
        {
            throw new SagaConcurrencyException(item.Correlation);
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(item.ExpectedVersion);

        var now = _clock.GetUtcNow();

        var sql = $"""
                   UPDATE {_tableName}
                   SET
                       is_completed = true,
                       optimistic_lock_version = optimistic_lock_version + 1,
                       last_applied_message_id = @applied_message_id,
                       updated_at = @now
                   WHERE correlation_id = @correlation_id
                       AND saga_type = @saga_type
                       AND tenant_id = @tenant_id
                       AND optimistic_lock_version = @expected_version
                       AND is_completed = false;
                   """;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(connection, sql);
        AddCorrelationKeyParameters(command, item.Correlation);
        command.Parameters.AddWithValue("expected_version", item.ExpectedVersion);
        AddOptionalParameter(command, "applied_message_id", NpgsqlDbType.Uuid, item.AppliedMessageId);
        command.Parameters.AddWithValue("now", now);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (updated == 0)
        {
            throw new SagaConcurrencyException(item.Correlation);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
        SagaQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(filter.Take, 0);

        var sql = $"""
                   SELECT correlation_id, saga_type, tenant_id, optimistic_lock_version, is_completed, created_at, updated_at
                   FROM {_tableName}
                   WHERE (@saga_type IS NULL OR saga_type = @saga_type)
                       AND (@correlation_id IS NULL OR correlation_id = @correlation_id)
                       AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
                       AND (@is_completed IS NULL OR is_completed = @is_completed)
                   ORDER BY updated_at DESC
                   LIMIT @take;
                   """;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(connection, sql);
        AddOptionalParameter(command, "saga_type", NpgsqlDbType.Text, filter.SagaDefinitionId);
        AddOptionalParameter(command, "correlation_id", NpgsqlDbType.Text, filter.CorrelationId);
        AddOptionalParameter(command, "tenant_id", NpgsqlDbType.Text, NormalizeTenantFilter(filter.TenantId));
        AddOptionalParameter(command, "is_completed", NpgsqlDbType.Boolean, filter.IsCompleted);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, filter.Take);

        List<SagaInstanceSummary> results = [];
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var tenantId = reader.GetString(2);

            results.Add(new SagaInstanceSummary
            {
                Correlation = new SagaCorrelation
                {
                    CorrelationId = reader.GetString(0),
                    SagaDefinitionId = reader.GetString(1),
                    TenantId = string.IsNullOrEmpty(tenantId) ? null : tenantId
                },
                Version = reader.GetInt32(3),
                IsCompleted = reader.GetBoolean(4),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var sql = $"""
                   DELETE FROM {_tableName}
                   WHERE (@saga_type IS NULL OR saga_type = @saga_type)
                       AND (@correlation_id IS NULL OR correlation_id = @correlation_id)
                       AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
                       AND (@is_completed IS NULL OR is_completed = @is_completed)
                       AND (@completed_before IS NULL OR (is_completed = true AND updated_at < @completed_before));
                   """;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = CreateCommand(connection, sql);
        AddOptionalParameter(command, "saga_type", NpgsqlDbType.Text, filter.SagaDefinitionId);
        AddOptionalParameter(command, "correlation_id", NpgsqlDbType.Text, filter.CorrelationId);
        AddOptionalParameter(command, "tenant_id", NpgsqlDbType.Text, NormalizeTenantFilter(filter.TenantId));
        AddOptionalParameter(command, "is_completed", NpgsqlDbType.Boolean, filter.IsCompleted);
        AddOptionalParameter(command, "completed_before", NpgsqlDbType.TimestampTz, filter.CompletedBefore);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
    ///     Adds correlation key parameters to one command.
    /// </summary>
    /// <param name="command">The command receiving parameters.</param>
    /// <param name="correlation">The saga correlation.</param>
    private static void AddCorrelationKeyParameters(NpgsqlCommand command, SagaCorrelation correlation)
    {
        command.Parameters.AddWithValue("correlation_id", correlation.CorrelationId);
        command.Parameters.AddWithValue("saga_type", correlation.SagaDefinitionId);
        command.Parameters.AddWithValue("tenant_id", NormalizeTenantId(correlation.TenantId));
    }

    /// <summary>
    ///     Adds correlation, state, and timestamp parameters to one command.
    /// </summary>
    /// <param name="command">The command receiving parameters.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="stateJson">The serialized state JSON.</param>
    /// <param name="now">The current timestamp.</param>
    /// <param name="appliedMessageId">The durable inbox message identifier applied by this save.</param>
    private static void AddCorrelationParameters(
        NpgsqlCommand command,
        SagaCorrelation correlation,
        string stateJson,
        DateTimeOffset now,
        Guid? appliedMessageId)
    {
        AddCorrelationKeyParameters(command, correlation);

        var stateParameter = command.Parameters.Add("state_json", NpgsqlDbType.Jsonb);
        stateParameter.Value = stateJson;
        AddOptionalParameter(command, "applied_message_id", NpgsqlDbType.Uuid, appliedMessageId);
        command.Parameters.AddWithValue("now", now);
    }

    /// <summary>
    ///     Adds a nullable parameter with an explicit PostgreSQL type.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name without the SQL prefix.</param>
    /// <param name="type">The PostgreSQL parameter type.</param>
    /// <param name="value">The parameter value, or <see langword="null" /> to bind a database null.</param>
    private static void AddOptionalParameter(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }

    /// <summary>
    ///     Preserves an omitted tenant filter while normalizing an explicit unscoped tenant.
    /// </summary>
    /// <param name="tenantId">The optional tenant filter.</param>
    /// <returns>
    ///     <see langword="null" /> when the query should include all tenants; otherwise, the normalized tenant identifier.
    /// </returns>
    private static string? NormalizeTenantFilter(string? tenantId)
    {
        return tenantId is null ? null : NormalizeTenantId(tenantId);
    }

    /// <summary>
    ///     Normalizes tenant identifiers for primary-key storage.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The normalized tenant identifier stored in saga rows.</returns>
    private static string NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? string.Empty : tenantId;
    }
}
