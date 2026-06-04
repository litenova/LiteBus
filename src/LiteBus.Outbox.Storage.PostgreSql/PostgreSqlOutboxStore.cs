using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     PostgreSQL outbox store backed by raw Npgsql commands.
/// </summary>
/// <remarks>
///     <para>
///         The store implements the writer, lease, and state roles against one table because the PostgreSQL transaction
///         boundary is the shared resource. Consumers still depend on the narrow role interfaces so writers, processors,
///         and tests can use the smallest required capability.
///     </para>
///     <para>
///         Leasing uses `FOR UPDATE SKIP LOCKED` to let multiple publishers claim different messages concurrently.
///         Expired publishing leases are eligible for another publisher, which gives at-least-once publication after
///         worker failure.
///     </para>
///     <para>
///         The default store opens its own connection per call. Use
///         <see cref="UseExistingConnection(NpgsqlConnection, NpgsqlTransaction)" /> when outbox writes must share the
///         caller's PostgreSQL transaction. Without that overload, <see cref="IOutbox.AddAsync" /> commits in a separate
///         transaction from manual SQL or ADO.NET work.
///     </para>
/// </remarks>
public sealed class PostgreSqlOutboxStore : IOutboxStore, IOutboxLeaseStore, IOutboxStateStore
{
    /// <summary>
    ///     The PostgreSQL data source used to open commands against the outbox table.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The quoted qualified outbox table name built from store options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     The existing open PostgreSQL connection used when callers provide an external transaction boundary.
    /// </summary>
    private readonly NpgsqlConnection? _transactionConnection;

    /// <summary>
    ///     The existing PostgreSQL transaction used for command execution when provided by the caller.
    /// </summary>
    private readonly NpgsqlTransaction? _transaction;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxStore" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The store options.</param>
    public PostgreSqlOutboxStore(NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlOutboxStoreOptions();
        _dataSource = dataSource;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxStore" /> class bound to an existing transaction.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="tableName">The fully qualified outbox table name.</param>
    /// <param name="transactionConnection">The existing open PostgreSQL connection.</param>
    /// <param name="transaction">The existing PostgreSQL transaction.</param>
    private PostgreSqlOutboxStore(
        NpgsqlDataSource dataSource,
        string tableName,
        NpgsqlConnection? transactionConnection,
        NpgsqlTransaction? transaction)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _transactionConnection = transactionConnection;
        _transaction = transaction;
    }

    /// <summary>
    ///     Returns a store that executes commands on an existing PostgreSQL connection and transaction.
    /// </summary>
    /// <param name="connection">The existing open connection owned by the caller.</param>
    /// <param name="transaction">The transaction that should contain outbox writes.</param>
    /// <returns>A store instance bound to the supplied connection and transaction.</returns>
    public PostgreSqlOutboxStore UseExistingConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException("The supplied transaction must belong to the supplied connection.", nameof(transaction));
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("The supplied connection must already be open.");
        }

        return new PostgreSqlOutboxStore(_dataSource, _tableName, connection, transaction);
    }

    /// <inheritdoc />
    public async Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var sql = $"""
                  INSERT INTO {_tableName} (
                      message_id,
                      contract_name,
                      contract_version,
                      payload,
                      topic,
                      created_at,
                      visible_after,
                      status,
                      attempt_count,
                      lease_owner,
                      lease_expires_at,
                      last_error,
                      correlation_id,
                      causation_id,
                      tenant_id,
                      idempotency_key,
                      trace_context)
                  VALUES (
                      @message_id,
                      @contract_name,
                      @contract_version,
                      @payload,
                      @topic,
                      @created_at,
                      @visible_after,
                      @status,
                      @attempt_count,
                      @lease_owner,
                      @lease_expires_at,
                      @last_error,
                      @correlation_id,
                      @causation_id,
                      @tenant_id,
                      @idempotency_key,
                      @trace_context)
                  ON CONFLICT DO NOTHING
                  RETURNING
                      message_id,
                      contract_name,
                      contract_version,
                      payload::text,
                      topic,
                      created_at,
                      visible_after,
                      status,
                      attempt_count,
                      lease_owner,
                      lease_expires_at,
                      last_error,
                      correlation_id,
                      causation_id,
                      tenant_id,
                      idempotency_key,
                      trace_context::text;
                  """;

        await using var command = CreateCommand(sql);
        AddEnvelopeParameters(command, envelope);

        var storedEnvelope = await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false);

        return storedEnvelope ?? await FindExistingAsync(envelope.Id, envelope.IdempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(OutboxLeaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sql = $"""
                  WITH candidates AS (
                      SELECT message_id
                      FROM {_tableName}
                      WHERE
                          ((status IN (@pending_status, @failed_status) AND (visible_after IS NULL OR visible_after <= @now))
                           OR (status = @publishing_status AND lease_expires_at IS NOT NULL AND lease_expires_at <= @now))
                      ORDER BY created_at ASC
                      LIMIT @batch_size
                      FOR UPDATE SKIP LOCKED
                  )
                  UPDATE {_tableName} AS outbox
                  SET
                      status = @publishing_status,
                      lease_owner = @lease_owner,
                      lease_expires_at = @lease_expires_at,
                      attempt_count = outbox.attempt_count + 1
                  FROM candidates
                  WHERE outbox.message_id = candidates.message_id
                  RETURNING
                      outbox.message_id,
                      outbox.contract_name,
                      outbox.contract_version,
                      outbox.payload::text,
                      outbox.topic,
                      outbox.created_at,
                      outbox.visible_after,
                      outbox.status,
                      outbox.attempt_count,
                      outbox.lease_owner,
                      outbox.lease_expires_at,
                      outbox.last_error,
                      outbox.correlation_id,
                      outbox.causation_id,
                      outbox.tenant_id,
                      outbox.trace_context::text;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)OutboxStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int)OutboxStatus.Failed);
        command.Parameters.AddWithValue("publishing_status", (int)OutboxStatus.Publishing);
        command.Parameters.AddWithValue("now", request.Now);
        command.Parameters.AddWithValue("batch_size", request.BatchSize);
        command.Parameters.AddWithValue("lease_owner", request.LeaseOwner);
        command.Parameters.AddWithValue("lease_expires_at", request.Now.Add(request.LeaseDuration));

        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @published_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE message_id = @message_id;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("published_status", (int)OutboxStatus.Published);
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(OutboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @failed_status,
                      visible_after = @visible_after,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = @last_error
                  WHERE message_id = @message_id;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("failed_status", (int)OutboxStatus.Failed);
        command.Parameters.AddWithValue("visible_after", (object?)failure.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", failure.Error);
        command.Parameters.AddWithValue("message_id", failure.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MoveToDeadLetterAsync(OutboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @dead_lettered_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = @last_error
                  WHERE message_id = @message_id;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("dead_lettered_status", (int)OutboxStatus.DeadLettered);
        command.Parameters.AddWithValue("last_error", deadLetter.Reason);
        command.Parameters.AddWithValue("message_id", deadLetter.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkPublishedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return;
        }

        if (messageIds.Count == 1)
        {
            await MarkPublishedAsync(messageIds[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @published_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE message_id = ANY(@message_ids);
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("published_status", (int)OutboxStatus.Published);
        command.Parameters.AddWithValue("message_ids", messageIds);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(IReadOnlyList<OutboxEnvelopeFailure> failures, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 0)
        {
            return;
        }

        if (failures.Count == 1)
        {
            await MarkFailedAsync(failures[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        var ids = new Guid[failures.Count];
        var visibleAfter = new DateTimeOffset?[failures.Count];
        var errors = new string[failures.Count];

        for (var index = 0; index < failures.Count; index++)
        {
            ids[index] = failures[index].Id;
            visibleAfter[index] = failures[index].VisibleAfter;
            errors[index] = failures[index].Error;
        }

        var sql = $"""
                  UPDATE {_tableName} AS outbox
                  SET
                      status = @failed_status,
                      visible_after = batch.visible_after,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = batch.last_error
                  FROM unnest(@message_ids, @visible_after, @last_errors) AS batch(message_id, visible_after, last_error)
                  WHERE outbox.message_id = batch.message_id;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("failed_status", (int)OutboxStatus.Failed);
        command.Parameters.AddWithValue("message_ids", ids);
        command.Parameters.AddWithValue("visible_after", visibleAfter);
        command.Parameters.AddWithValue("last_errors", errors);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @pending_status,
                      visible_after = NULL,
                      attempt_count = 0,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE message_id = @message_id AND status = @dead_lettered_status;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)OutboxStatus.Pending);
        command.Parameters.AddWithValue("dead_lettered_status", (int)OutboxStatus.DeadLettered);
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  DELETE FROM {_tableName}
                  WHERE status = @published_status AND created_at < @older_than;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("published_status", (int)OutboxStatus.Published);
        command.Parameters.AddWithValue("older_than", olderThan);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  SELECT status, COUNT(*)::int
                  FROM {_tableName}
                  GROUP BY status;
                  """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<OutboxStatus, int>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[(OutboxStatus)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    /// <summary>
    ///     Reads the row that caused an idempotent insert to be skipped.
    /// </summary>
    /// <param name="messageId">The message id from the attempted insert.</param>
    /// <param name="idempotencyKey">The optional idempotency key from the attempted insert.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The existing stored envelope that should be returned to the writer.</returns>
    private async Task<OutboxEnvelope> FindExistingAsync(
        Guid messageId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                  SELECT
                      message_id,
                      contract_name,
                      contract_version,
                      payload::text,
                      topic,
                      created_at,
                      visible_after,
                      status,
                      attempt_count,
                      lease_owner,
                      lease_expires_at,
                      last_error,
                      correlation_id,
                      causation_id,
                      tenant_id,
                      idempotency_key,
                      trace_context::text
                  FROM {_tableName}
                  WHERE message_id = @message_id
                     OR (@idempotency_key IS NOT NULL AND idempotency_key = @idempotency_key)
                  LIMIT 1;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("idempotency_key", (object?)idempotencyKey ?? DBNull.Value);

        return await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The outbox insert was skipped but the existing message could not be found.");
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command with the database types expected by the outbox table.
    /// </summary>
    /// <param name="command">The command that will insert an outbox row.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, OutboxEnvelope envelope)
    {
        command.Parameters.AddWithValue("message_id", envelope.Id);
        command.Parameters.AddWithValue("contract_name", envelope.ContractName);
        command.Parameters.AddWithValue("contract_version", envelope.ContractVersion);

        var payloadParameter = command.Parameters.Add("payload", NpgsqlDbType.Jsonb);
        payloadParameter.Value = envelope.Payload;

        command.Parameters.AddWithValue("topic", (object?)envelope.Topic ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", envelope.CreatedAt);
        command.Parameters.AddWithValue("visible_after", (object?)envelope.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (int)envelope.Status);
        command.Parameters.AddWithValue("attempt_count", envelope.AttemptCount);
        command.Parameters.AddWithValue("lease_owner", (object?)envelope.LeaseOwner ?? DBNull.Value);
        command.Parameters.AddWithValue("lease_expires_at", (object?)envelope.LeaseExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", (object?)envelope.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)envelope.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causation_id", (object?)envelope.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("tenant_id", (object?)envelope.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("idempotency_key", (object?)envelope.IdempotencyKey ?? DBNull.Value);

        var traceContextParameter = command.Parameters.Add("trace_context", NpgsqlDbType.Jsonb);
        traceContextParameter.Value = string.IsNullOrWhiteSpace(envelope.TraceContext) ? DBNull.Value : envelope.TraceContext;
    }

    /// <summary>
    ///     Executes a command that returns zero or one outbox envelope.
    /// </summary>
    /// <param name="command">The query command.</param>
    /// <param name="cancellationToken">A token used to cancel reader execution.</param>
    /// <returns>The envelope when a row is returned; otherwise, null.</returns>
    private static async Task<OutboxEnvelope?> ReadSingleOrDefaultAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEnvelope(reader)
            : null;
    }

    /// <summary>
    ///     Executes a command that returns a batch of outbox envelopes.
    /// </summary>
    /// <param name="command">The query command.</param>
    /// <param name="cancellationToken">A token used to cancel reader execution.</param>
    /// <returns>The envelopes returned by the database in query order.</returns>
    private static async Task<IReadOnlyList<OutboxEnvelope>> ReadManyAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var envelopes = new List<OutboxEnvelope>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            envelopes.Add(ReadEnvelope(reader));
        }

        return envelopes;
    }

    /// <summary>
    ///     Maps the current data-reader row to an outbox envelope.
    /// </summary>
    /// <param name="reader">The reader positioned on an outbox row.</param>
    /// <returns>The mapped envelope.</returns>
    private static OutboxEnvelope ReadEnvelope(NpgsqlDataReader reader)
    {
        return new OutboxEnvelope
        {
            Id = reader.GetGuid(0),
            ContractName = reader.GetString(1),
            ContractVersion = reader.GetInt32(2),
            Payload = reader.GetString(3),
            Topic = GetNullableString(reader, 4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
            VisibleAfter = GetNullable<DateTimeOffset>(reader, 6),
            Status = (OutboxStatus)reader.GetInt32(7),
            AttemptCount = reader.GetInt32(8),
            LeaseOwner = GetNullableString(reader, 9),
            LeaseExpiresAt = GetNullable<DateTimeOffset>(reader, 10),
            LastError = GetNullableString(reader, 11),
            CorrelationId = GetNullableString(reader, 12),
            CausationId = GetNullableString(reader, 13),
            TenantId = GetNullableString(reader, 14),
            IdempotencyKey = GetNullableString(reader, 15),
            TraceContext = GetNullableString(reader, 16)
        };
    }

    /// <summary>
    ///     Reads a nullable value type from the current row.
    /// </summary>
    /// <typeparam name="T">The value type to read.</typeparam>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value when the column is not database null; otherwise, null.</returns>
    private static T? GetNullable<T>(NpgsqlDataReader reader, int ordinal)
        where T : struct
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
    }

    /// <summary>
    ///     Reads a nullable string from the current row.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The string when the column is not database null; otherwise, null.</returns>
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    ///     Creates a command against the configured data source or caller-supplied transaction.
    /// </summary>
    /// <param name="sql">The SQL command text.</param>
    /// <returns>The initialized PostgreSQL command.</returns>
    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// <summary>
    ///     Creates a command object for the current execution mode.
    /// </summary>
    /// <returns>The initialized PostgreSQL command.</returns>
    private NpgsqlCommand CreateCommand()
    {
        if (_transactionConnection is null || _transaction is null)
        {
            return _dataSource.CreateCommand();
        }

        var command = _transactionConnection.CreateCommand();
        command.Transaction = _transaction;
        return command;
    }
}