using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Outbox.PostgreSql;

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
/// </remarks>
public sealed class PostgreSqlOutboxStore : IOutboxMessageWriter, IOutboxMessageLeaseStore, IOutboxMessageStateStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;

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

    /// <inheritdoc />
    public async Task<OutboxMessageEnvelope> AddAsync(OutboxMessageEnvelope envelope, CancellationToken cancellationToken = default)
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
                      tenant_id)
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
                      @tenant_id)
                  ON CONFLICT (message_id) DO NOTHING
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
                      tenant_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        AddEnvelopeParameters(command, envelope);

        var storedEnvelope = await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false);

        return storedEnvelope ?? await FindExistingAsync(envelope.MessageId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessageEnvelope>> LeasePendingAsync(OutboxLeaseRequest request, CancellationToken cancellationToken = default)
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
                      outbox.tenant_id;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)OutboxMessageStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int)OutboxMessageStatus.Failed);
        command.Parameters.AddWithValue("publishing_status", (int)OutboxMessageStatus.Publishing);
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

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("published_status", (int)OutboxMessageStatus.Published);
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(OutboxMessageFailure failure, CancellationToken cancellationToken = default)
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
        command.Parameters.AddWithValue("failed_status", (int)OutboxMessageStatus.Failed);
        command.Parameters.AddWithValue("visible_after", (object?)failure.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", failure.Error);
        command.Parameters.AddWithValue("message_id", failure.MessageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MoveToDeadLetterAsync(OutboxMessageDeadLetter deadLetter, CancellationToken cancellationToken = default)
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
        command.Parameters.AddWithValue("dead_lettered_status", (int)OutboxMessageStatus.DeadLettered);
        command.Parameters.AddWithValue("last_error", deadLetter.Reason);
        command.Parameters.AddWithValue("message_id", deadLetter.MessageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads the row that caused an idempotent insert to be skipped.
    /// </summary>
    /// <param name="messageId">The message id from the attempted insert.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The existing stored envelope that should be returned to the writer.</returns>
    private async Task<OutboxMessageEnvelope> FindExistingAsync(Guid messageId, CancellationToken cancellationToken)
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
                      tenant_id
                  FROM {_tableName}
                  WHERE message_id = @message_id
                  LIMIT 1;
                  """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("message_id", messageId);

        return await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The outbox insert was skipped but the existing message could not be found.");
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command with the database types expected by the outbox table.
    /// </summary>
    /// <param name="command">The command that will insert an outbox row.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, OutboxMessageEnvelope envelope)
    {
        command.Parameters.AddWithValue("message_id", envelope.MessageId);
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
    }

    /// <summary>
    ///     Executes a command that returns zero or one outbox envelope.
    /// </summary>
    /// <param name="command">The query command.</param>
    /// <param name="cancellationToken">A token used to cancel reader execution.</param>
    /// <returns>The envelope when a row is returned; otherwise, null.</returns>
    private static async Task<OutboxMessageEnvelope?> ReadSingleOrDefaultAsync(NpgsqlCommand command, CancellationToken cancellationToken)
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
    private static async Task<IReadOnlyList<OutboxMessageEnvelope>> ReadManyAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var envelopes = new List<OutboxMessageEnvelope>();

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
    private static OutboxMessageEnvelope ReadEnvelope(NpgsqlDataReader reader)
    {
        return new OutboxMessageEnvelope
        {
            MessageId = reader.GetGuid(0),
            ContractName = reader.GetString(1),
            ContractVersion = reader.GetInt32(2),
            Payload = reader.GetString(3),
            Topic = GetNullableString(reader, 4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
            VisibleAfter = GetNullable<DateTimeOffset>(reader, 6),
            Status = (OutboxMessageStatus)reader.GetInt32(7),
            AttemptCount = reader.GetInt32(8),
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