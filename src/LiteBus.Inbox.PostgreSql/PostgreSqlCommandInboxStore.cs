using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     PostgreSQL command inbox store backed by raw Npgsql commands.
/// </summary>
/// <remarks>
///     <para>
///         The store implements the writer, lease, and state roles against one table because the PostgreSQL transaction
///         boundary is the shared resource. Consumers still depend on the narrow role interfaces so scheduling code,
///         processors, and tests can use the smallest required capability.
///     </para>
///     <para>
///         Leasing uses `FOR UPDATE SKIP LOCKED` to let multiple processors claim different commands concurrently.
///         Expired processing leases are eligible for another worker, which gives at-least-once execution after worker
///         failure.
///     </para>
/// </remarks>
public sealed class PostgreSqlCommandInboxStore : ICommandInboxWriter, ICommandInboxLeaseStore, ICommandInboxStateStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlCommandInboxStore" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The store options.</param>
    public PostgreSqlCommandInboxStore(NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlInboxStoreOptions();

        _dataSource = dataSource;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
    }

    /// <inheritdoc />
    public async Task<InboxCommandEnvelope> AddAsync(InboxCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var sql = $"""
                  INSERT INTO {_tableName} (
                      command_id,
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
                      tenant_id)
                  VALUES (
                      @command_id,
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
                      @tenant_id)
                  -- No conflict target: catches both the command_id primary key violation and the
                  -- unique partial index on idempotency_key. Both represent the same idempotent intent.
                  ON CONFLICT DO NOTHING
                  RETURNING
                      command_id,
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
                      tenant_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        AddEnvelopeParameters(command, envelope);

        var storedEnvelope = await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false);

        return storedEnvelope ?? await FindExistingAsync(envelope.CommandId, envelope.IdempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxCommandEnvelope>> LeasePendingAsync(InboxLeaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sql = $"""
                  WITH candidates AS (
                      SELECT command_id
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
                  WHERE inbox.command_id = candidates.command_id
                  RETURNING
                      inbox.command_id,
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
                      inbox.tenant_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)InboxCommandStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int)InboxCommandStatus.Failed);
        command.Parameters.AddWithValue("processing_status", (int)InboxCommandStatus.Processing);
        command.Parameters.AddWithValue("now", request.Now);
        command.Parameters.AddWithValue("batch_size", request.BatchSize);
        command.Parameters.AddWithValue("lease_owner", request.LeaseOwner);
        command.Parameters.AddWithValue("lease_expires_at", request.Now.Add(request.LeaseDuration));

        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @completed_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE command_id = @command_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("completed_status", (int)InboxCommandStatus.Completed);
        command.Parameters.AddWithValue("command_id", commandId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(InboxCommandFailure failure, CancellationToken cancellationToken = default)
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
                  WHERE command_id = @command_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("failed_status", (int)InboxCommandStatus.Failed);
        command.Parameters.AddWithValue("visible_after", (object?)failure.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", failure.Error);
        command.Parameters.AddWithValue("command_id", failure.CommandId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MoveToDeadLetterAsync(InboxCommandDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @dead_lettered_status,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = @last_error
                  WHERE command_id = @command_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("dead_lettered_status", (int)InboxCommandStatus.DeadLettered);
        command.Parameters.AddWithValue("last_error", deadLetter.Reason);
        command.Parameters.AddWithValue("command_id", deadLetter.CommandId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads the row that caused an idempotent insert to be skipped.
    /// </summary>
    /// <param name="commandId">The command id from the attempted insert.</param>
    /// <param name="idempotencyKey">The idempotency key from the attempted insert, when one was supplied.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The existing stored envelope that should be returned to the scheduler.</returns>
    private async Task<InboxCommandEnvelope> FindExistingAsync(Guid commandId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var sql = $"""
                  SELECT
                      command_id,
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
                      tenant_id
                  FROM {_tableName}
                  WHERE command_id = @command_id
                     OR (@idempotency_key IS NOT NULL AND idempotency_key = @idempotency_key)
                  ORDER BY CASE WHEN command_id = @command_id THEN 0 ELSE 1 END
                  LIMIT 1;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.AddWithValue("idempotency_key", (object?)idempotencyKey ?? DBNull.Value);

        return await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The command inbox insert was skipped but the existing command could not be found.");
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command with the database types expected by the inbox table.
    /// </summary>
    /// <param name="command">The command that will insert an inbox row.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, InboxCommandEnvelope envelope)
    {
        command.Parameters.AddWithValue("command_id", envelope.CommandId);
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
    }

    /// <summary>
    ///     Executes a command that returns zero or one inbox envelope.
    /// </summary>
    /// <param name="command">The query command.</param>
    /// <param name="cancellationToken">A token used to cancel reader execution.</param>
    /// <returns>The envelope when a row is returned; otherwise, null.</returns>
    private static async Task<InboxCommandEnvelope?> ReadSingleOrDefaultAsync(NpgsqlCommand command, CancellationToken cancellationToken)
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
    private static async Task<IReadOnlyList<InboxCommandEnvelope>> ReadManyAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var envelopes = new List<InboxCommandEnvelope>();

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
    private static InboxCommandEnvelope ReadEnvelope(NpgsqlDataReader reader)
    {
        return new InboxCommandEnvelope
        {
            CommandId = reader.GetGuid(0),
            ContractName = reader.GetString(1),
            ContractVersion = reader.GetInt32(2),
            Payload = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            VisibleAfter = GetNullable<DateTimeOffset>(reader, 5),
            AttemptCount = reader.GetInt32(6),
            Status = (InboxCommandStatus)reader.GetInt32(7),
            IdempotencyKey = GetNullableString(reader, 8),
            LeaseOwner = GetNullableString(reader, 9),
            LeaseExpiresAt = GetNullable<DateTimeOffset>(reader, 10),
            LastError = GetNullableString(reader, 11),
            CorrelationId = GetNullableString(reader, 12),
            CausationId = GetNullableString(reader, 13),
            TenantId = GetNullableString(reader, 14)
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