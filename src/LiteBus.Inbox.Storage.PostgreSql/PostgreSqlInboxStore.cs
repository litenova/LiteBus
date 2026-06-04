using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     PostgreSQL inbox store backed by raw Npgsql commands.
/// </summary>
/// <remarks>
///     <para>
///         The store implements the writer, lease, and state roles against one table because the PostgreSQL transaction
///         boundary is the shared resource. Consumers still depend on the narrow role interfaces so scheduling code,
///         processors, and tests can use the smallest required capability.
///     </para>
///     <para>
///         Leasing uses `FOR UPDATE SKIP LOCKED` to let multiple processors claim different messages concurrently.
///         Expired processing leases are eligible for another worker, which gives at-least-once execution after worker
///         failure.
///     </para>
/// </remarks>
public sealed class PostgreSqlInboxStore : IInboxStore, IInboxLeaseStore, IInboxStateStore
{
    /// <summary>
    ///     The PostgreSQL data source used to open commands against the inbox table.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The quoted qualified inbox table name built from store options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxStore" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The store options.</param>
    public PostgreSqlInboxStore(NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlInboxStoreOptions();

        _dataSource = dataSource;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
    }

    /// <inheritdoc />
    public async Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var sql = $"""
                  INSERT INTO {_tableName} (
                      message_id,
                      contract_name,
                      contract_version,
                      payload,
                      created_at,
                      visible_after,
                      attempt_count,
                      status,
                      idempotency_key,
                      lease_owner,
                      lease_expires_at,
                      last_error,
                      correlation_id,
                      causation_id,
                      tenant_id,
                      trace_context)
                  VALUES (
                      @message_id,
                      @contract_name,
                      @contract_version,
                      @payload,
                      @created_at,
                      @visible_after,
                      @attempt_count,
                      @status,
                      @idempotency_key,
                      @lease_owner,
                      @lease_expires_at,
                      @last_error,
                      @correlation_id,
                      @causation_id,
                      @tenant_id,
                      @trace_context)
                  -- No conflict target: catches both the message_id primary key violation and the
                  -- unique partial index on idempotency_key. Both represent the same idempotent intent.
                  ON CONFLICT DO NOTHING
                  RETURNING
                      message_id,
                      contract_name,
                      contract_version,
                      payload::text,
                      created_at,
                      visible_after,
                      attempt_count,
                      status,
                      idempotency_key,
                      lease_owner,
                      lease_expires_at,
                      last_error,
                      correlation_id,
                      causation_id,
                      tenant_id,
                      trace_context::text;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        AddEnvelopeParameters(command, envelope);

        var storedEnvelope = await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false);

        return storedEnvelope ?? await FindExistingAsync(envelope.Id, envelope.IdempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(InboxLeaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sql = $"""
                  WITH candidates AS (
                      SELECT message_id
                      FROM {_tableName}
                      WHERE
                          ((status IN (@pending_status, @failed_status) AND (visible_after IS NULL OR visible_after <= @now))
                           OR (status = @processing_status AND lease_expires_at IS NOT NULL AND lease_expires_at <= @now))
                      ORDER BY created_at ASC
                      LIMIT @batch_size
                      FOR UPDATE SKIP LOCKED
                  )
                  UPDATE {_tableName} AS inbox
                  SET
                      status = @processing_status,
                      lease_owner = @lease_owner,
                      lease_expires_at = @lease_expires_at,
                      attempt_count = inbox.attempt_count + 1
                  FROM candidates
                  WHERE inbox.message_id = candidates.message_id
                  RETURNING
                      inbox.message_id,
                      inbox.contract_name,
                      inbox.contract_version,
                      inbox.payload::text,
                      inbox.created_at,
                      inbox.visible_after,
                      inbox.attempt_count,
                      inbox.status,
                      inbox.idempotency_key,
                      inbox.lease_owner,
                      inbox.lease_expires_at,
                      inbox.last_error,
                      inbox.correlation_id,
                      inbox.causation_id,
                      inbox.tenant_id,
                      inbox.trace_context::text;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)InboxStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int)InboxStatus.Failed);
        command.Parameters.AddWithValue("processing_status", (int)InboxStatus.Processing);
        command.Parameters.AddWithValue("now", request.Now);
        command.Parameters.AddWithValue("batch_size", request.BatchSize);
        command.Parameters.AddWithValue("lease_owner", request.LeaseOwner);
        command.Parameters.AddWithValue("lease_expires_at", request.Now.Add(request.LeaseDuration));

        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @completed_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE message_id = @message_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("completed_status", (int)InboxStatus.Completed);
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
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

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("failed_status", (int)InboxStatus.Failed);
        command.Parameters.AddWithValue("visible_after", (object?)failure.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", failure.Error);
        command.Parameters.AddWithValue("message_id", failure.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
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

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("dead_lettered_status", (int)InboxStatus.DeadLettered);
        command.Parameters.AddWithValue("last_error", deadLetter.Reason);
        command.Parameters.AddWithValue("message_id", deadLetter.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return;
        }

        if (messageIds.Count == 1)
        {
            await MarkCompletedAsync(messageIds[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @completed_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE message_id = ANY(@message_ids);
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("completed_status", (int)InboxStatus.Completed);
        command.Parameters.AddWithValue("message_ids", messageIds);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(IReadOnlyList<InboxEnvelopeFailure> failures, CancellationToken cancellationToken = default)
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
                  UPDATE {_tableName} AS inbox
                  SET
                      status = @failed_status,
                      visible_after = batch.visible_after,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = batch.last_error
                  FROM unnest(@message_ids, @visible_after, @last_errors) AS batch(message_id, visible_after, last_error)
                  WHERE inbox.message_id = batch.message_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("failed_status", (int)InboxStatus.Failed);
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

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)InboxStatus.Pending);
        command.Parameters.AddWithValue("dead_lettered_status", (int)InboxStatus.DeadLettered);
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  DELETE FROM {_tableName}
                  WHERE status = @completed_status AND created_at < @older_than;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("completed_status", (int)InboxStatus.Completed);
        command.Parameters.AddWithValue("older_than", olderThan);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  SELECT status, COUNT(*)::int
                  FROM {_tableName}
                  GROUP BY status;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<InboxStatus, int>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[(InboxStatus)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    /// <summary>
    ///     Reads the row that caused an idempotent insert to be skipped.
    /// </summary>
    /// <param name="messageId">The message id from the attempted insert.</param>
    /// <param name="idempotencyKey">The idempotency key from the attempted insert, when one was supplied.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The existing stored envelope that should be returned to the scheduler.</returns>
    private async Task<InboxEnvelope> FindExistingAsync(Guid messageId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        string sql;
        await using var command = _dataSource.CreateCommand();
        command.Parameters.AddWithValue("message_id", messageId);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            sql = $"""
                   SELECT
                       message_id,
                       contract_name,
                       contract_version,
                       payload::text,
                       created_at,
                       visible_after,
                       attempt_count,
                       status,
                       idempotency_key,
                       lease_owner,
                       lease_expires_at,
                       last_error,
                       correlation_id,
                       causation_id,
                       tenant_id,
                       trace_context::text
                   FROM {_tableName}
                   WHERE message_id = @message_id
                   LIMIT 1;
                   """;
        }
        else
        {
            sql = $"""
                   SELECT
                       message_id,
                       contract_name,
                       contract_version,
                       payload::text,
                       created_at,
                       visible_after,
                       attempt_count,
                       status,
                       idempotency_key,
                       lease_owner,
                       lease_expires_at,
                       last_error,
                       correlation_id,
                       causation_id,
                       tenant_id,
                       trace_context::text
                   FROM {_tableName}
                   WHERE message_id = @message_id
                      OR idempotency_key = @idempotency_key
                   ORDER BY CASE WHEN message_id = @message_id THEN 0 ELSE 1 END
                   LIMIT 1;
                   """;

            command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        }

        command.CommandText = sql;

        return await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The inbox insert was skipped but the existing message could not be found.");
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command with the database types expected by the inbox table.
    /// </summary>
    /// <param name="command">The command that will insert an inbox row.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, InboxEnvelope envelope)
    {
        command.Parameters.AddWithValue("message_id", envelope.Id);
        command.Parameters.AddWithValue("contract_name", envelope.ContractName);
        command.Parameters.AddWithValue("contract_version", envelope.ContractVersion);

        var payloadParameter = command.Parameters.Add("payload", NpgsqlDbType.Jsonb);
        payloadParameter.Value = envelope.Payload;

        command.Parameters.AddWithValue("created_at", envelope.CreatedAt);
        command.Parameters.AddWithValue("visible_after", (object?)envelope.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("attempt_count", envelope.AttemptCount);
        command.Parameters.AddWithValue("status", (int)envelope.Status);
        command.Parameters.AddWithValue("idempotency_key", (object?)envelope.IdempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("lease_owner", (object?)envelope.LeaseOwner ?? DBNull.Value);
        command.Parameters.AddWithValue("lease_expires_at", (object?)envelope.LeaseExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", (object?)envelope.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)envelope.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causation_id", (object?)envelope.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("tenant_id", (object?)envelope.TenantId ?? DBNull.Value);

        var traceContextParameter = command.Parameters.Add("trace_context", NpgsqlDbType.Jsonb);
        traceContextParameter.Value = string.IsNullOrWhiteSpace(envelope.TraceContext) ? DBNull.Value : envelope.TraceContext;
    }

    /// <summary>
    ///     Executes a command that returns zero or one inbox envelope.
    /// </summary>
    /// <param name="command">The query command.</param>
    /// <param name="cancellationToken">A token used to cancel reader execution.</param>
    /// <returns>The envelope when a row is returned; otherwise, null.</returns>
    private static async Task<InboxEnvelope?> ReadSingleOrDefaultAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEnvelope(reader)
            : null;
    }

    /// <summary>
    ///     Executes a command that returns a batch of inbox envelopes.
    /// </summary>
    /// <param name="command">The query command.</param>
    /// <param name="cancellationToken">A token used to cancel reader execution.</param>
    /// <returns>The envelopes returned by the database in query order.</returns>
    private static async Task<IReadOnlyList<InboxEnvelope>> ReadManyAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var envelopes = new List<InboxEnvelope>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            envelopes.Add(ReadEnvelope(reader));
        }

        return envelopes;
    }

    /// <summary>
    ///     Maps the current data-reader row to an inbox envelope.
    /// </summary>
    /// <param name="reader">The reader positioned on an inbox row.</param>
    /// <returns>The mapped envelope.</returns>
    private static InboxEnvelope ReadEnvelope(NpgsqlDataReader reader)
    {
        return new InboxEnvelope
        {
            Id = reader.GetGuid(0),
            ContractName = reader.GetString(1),
            ContractVersion = reader.GetInt32(2),
            Payload = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            VisibleAfter = GetNullable<DateTimeOffset>(reader, 5),
            AttemptCount = reader.GetInt32(6),
            Status = (InboxStatus)reader.GetInt32(7),
            IdempotencyKey = GetNullableString(reader, 8),
            LeaseOwner = GetNullableString(reader, 9),
            LeaseExpiresAt = GetNullable<DateTimeOffset>(reader, 10),
            LastError = GetNullableString(reader, 11),
            CorrelationId = GetNullableString(reader, 12),
            CausationId = GetNullableString(reader, 13),
            TenantId = GetNullableString(reader, 14),
            TraceContext = GetNullableString(reader, 15)
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
}