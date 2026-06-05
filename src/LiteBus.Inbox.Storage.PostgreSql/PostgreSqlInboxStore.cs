using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
public sealed class PostgreSqlInboxStore :
    IInboxProcessingStore,
    IInboxOperationsStore
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
    ///     The existing open PostgreSQL connection used when callers provide an external transaction boundary.
    /// </summary>
    private readonly NpgsqlConnection? _transactionConnection;

    /// <summary>
    ///     The existing PostgreSQL transaction used for command execution when provided by the caller.
    /// </summary>
    private readonly NpgsqlTransaction? _transaction;

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

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxStore" /> class bound to an existing transaction.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="tableName">The fully qualified inbox table name.</param>
    /// <param name="transactionConnection">The existing open PostgreSQL connection.</param>
    /// <param name="transaction">The existing PostgreSQL transaction.</param>
    private PostgreSqlInboxStore(
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
    /// <param name="transaction">The transaction that should contain inbox writes.</param>
    /// <returns>A store instance bound to the supplied connection and transaction.</returns>
    public PostgreSqlInboxStore UseExistingConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)
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

        return new PostgreSqlInboxStore(_dataSource, _tableName, connection, transaction);
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
                      trace_context::text,
                      completed_at;
                  """;

        await using var command = CreateCommand(sql);
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
                          (@tenant_id IS NULL OR tenant_id = @tenant_id)
                          AND ((status IN (@pending_status, @failed_status) AND (visible_after IS NULL OR visible_after <= @now))
                           OR (status = @processing_status AND lease_expires_at IS NOT NULL AND lease_expires_at <= @now)
                           OR (status = @processing_status AND lease_expires_at IS NULL AND created_at < @stale_cutoff))
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
                      inbox.trace_context::text,
                      inbox.completed_at;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)InboxStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int)InboxStatus.Failed);
        command.Parameters.AddWithValue("processing_status", (int)InboxStatus.Processing);
        command.Parameters.AddWithValue("now", request.Now);
        command.Parameters.AddWithValue("batch_size", request.BatchSize);
        command.Parameters.AddWithValue("lease_owner", request.LeaseOwner);
        command.Parameters.AddWithValue("lease_expires_at", request.Now.Add(request.LeaseDuration));
        command.Parameters.AddWithValue("stale_cutoff", request.Now.Add(-request.LeaseDuration));
        AddNullableTextParameter(command, "tenant_id", request.TenantId);

        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RenewLeaseAsync(
        Guid messageId,
        string leaseOwner,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);

        var sql = $"""
                  UPDATE {_tableName}
                  SET lease_expires_at = @lease_expires_at
                  WHERE message_id = @message_id
                      AND status = @processing_status
                      AND lease_owner = @lease_owner;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("lease_expires_at", expiresAt);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("processing_status", (int)InboxStatus.Processing);
        command.Parameters.AddWithValue("lease_owner", leaseOwner);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Array.Empty<InboxEnvelope>();
        }

        if (envelopes.Count == 1)
        {
            return [await AddAsync(envelopes[0], cancellationToken).ConfigureAwait(false)];
        }

        var valueClauses = new StringBuilder();

        for (var index = 0; index < envelopes.Count; index++)
        {
            if (index > 0)
            {
                valueClauses.Append(',');
            }

            valueClauses.Append(
                $"(@message_id_{index}, @contract_name_{index}, @contract_version_{index}, @payload_{index}, " +
                $"@created_at_{index}, @visible_after_{index}, @attempt_count_{index}, @status_{index}, " +
                $"@idempotency_key_{index}, @lease_owner_{index}, @lease_expires_at_{index}, @last_error_{index}, " +
                $"@correlation_id_{index}, @causation_id_{index}, @tenant_id_{index}, @trace_context_{index})");
        }

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
                  VALUES {valueClauses}
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
                      trace_context::text,
                      completed_at;
                  """;

        await using var command = CreateCommand(sql);

        for (var index = 0; index < envelopes.Count; index++)
        {
            AddEnvelopeParameters(command, envelopes[index], index);
        }

        var inserted = await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
        var insertedById = inserted.ToDictionary(envelope => envelope.Id);
        var stored = new InboxEnvelope[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            var envelope = envelopes[index];

            stored[index] = insertedById.TryGetValue(envelope.Id, out var accepted)
                ? accepted
                : await FindExistingAsync(envelope.Id, envelope.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        }

        return stored;
    }

    /// <inheritdoc />
    public async Task PersistAsync(
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return;
        }

        List<InboxEnvelope>? completed = null;
        List<InboxEnvelope>? failed = null;
        List<InboxEnvelope>? deadLettered = null;

        foreach (var envelope in envelopes)
        {
            switch (envelope.Status)
            {
                case InboxStatus.Completed:
                    (completed ??= []).Add(envelope);
                    break;

                case InboxStatus.Failed:
                    (failed ??= []).Add(envelope);
                    break;

                case InboxStatus.DeadLettered:
                    (deadLettered ??= []).Add(envelope);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Envelope '{envelope.Id}' has unexpected status '{envelope.Status}' in PersistAsync. " +
                        "Only Completed, Failed, and DeadLettered are valid outcomes.");
            }
        }

        await PersistTerminalGroupsAsync(completed, failed, deadLettered, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists grouped terminal envelopes inside one PostgreSQL transaction when the store is not caller-bound.
    /// </summary>
    /// <param name="completed">The completed envelopes to persist, if any.</param>
    /// <param name="failed">The failed envelopes to persist, if any.</param>
    /// <param name="deadLettered">The dead-lettered envelopes to persist, if any.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private async Task PersistTerminalGroupsAsync(
        IReadOnlyList<InboxEnvelope>? completed,
        IReadOnlyList<InboxEnvelope>? failed,
        IReadOnlyList<InboxEnvelope>? deadLettered,
        CancellationToken cancellationToken)
    {
        if (_transactionConnection is null || _transaction is null)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var scopedStore = new PostgreSqlInboxStore(_dataSource, _tableName, connection, transaction);
            await scopedStore.PersistTerminalGroupsAsync(completed, failed, deadLettered, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (completed is not null)
        {
            await PersistCompletedAsync(completed, cancellationToken).ConfigureAwait(false);
        }

        if (failed is not null)
        {
            await PersistFailedAsync(failed, cancellationToken).ConfigureAwait(false);
        }

        if (deadLettered is not null)
        {
            await PersistDeadLetteredAsync(deadLettered, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Persists completed status for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The completed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private async Task PersistCompletedAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 1)
        {
            var envelope = envelopes[0];
            var sql = $"""
                      UPDATE {_tableName}
                      SET
                          status = @completed_status,
                          lease_owner = NULL,
                          lease_expires_at = NULL,
                          last_error = NULL,
                          completed_at = @completed_at
                      WHERE message_id = @message_id
                          AND status = @in_flight_status
                          AND lease_owner = @owner;
                      """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("completed_status", (int)InboxStatus.Completed);
            command.Parameters.AddWithValue("in_flight_status", (int)InboxStatus.Processing);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            command.Parameters.AddWithValue("completed_at", ResolveCompletedAt(envelope));
            await ExecuteTerminalUpdateAsync(command, cancellationToken).ConfigureAwait(false);
            return;
        }

        var ids = new Guid[envelopes.Count];
        var owners = new string[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            ids[index] = envelopes[index].Id;
            owners[index] = envelopes[index].LeaseOwner!;
        }

        var batchSql = $"""
                       UPDATE {_tableName} AS inbox
                       SET
                           status = @completed_status,
                           lease_owner = NULL,
                           lease_expires_at = NULL,
                           last_error = NULL,
                           completed_at = NOW()
                       FROM unnest(@message_ids, @lease_owners) AS batch(message_id, lease_owner)
                       WHERE inbox.message_id = batch.message_id
                           AND inbox.status = @in_flight_status
                           AND inbox.lease_owner = batch.lease_owner;
                       """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("completed_status", (int)InboxStatus.Completed);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int)InboxStatus.Processing);
        batchCommand.Parameters.AddWithValue("message_ids", ids);
        batchCommand.Parameters.AddWithValue("lease_owners", owners);
        await ExecuteTerminalUpdateAsync(batchCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists failed status and retry metadata for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The failed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private async Task PersistFailedAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 1)
        {
            var envelope = envelopes[0];
            var sql = $"""
                      UPDATE {_tableName}
                      SET
                          status = @failed_status,
                          visible_after = @visible_after,
                          lease_owner = NULL,
                          lease_expires_at = NULL,
                          last_error = @last_error
                      WHERE message_id = @message_id
                          AND status = @in_flight_status
                          AND lease_owner = @owner;
                      """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("failed_status", (int)InboxStatus.Failed);
            command.Parameters.AddWithValue("in_flight_status", (int)InboxStatus.Processing);
            command.Parameters.AddWithValue("visible_after", (object?)envelope.VisibleAfter ?? DBNull.Value);
            command.Parameters.AddWithValue("last_error", envelope.LastError!);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            await ExecuteTerminalUpdateAsync(command, cancellationToken).ConfigureAwait(false);
            return;
        }

        var ids = new Guid[envelopes.Count];
        var owners = new string[envelopes.Count];
        var visibleAfter = new DateTimeOffset?[envelopes.Count];
        var errors = new string[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            ids[index] = envelopes[index].Id;
            owners[index] = envelopes[index].LeaseOwner!;
            visibleAfter[index] = envelopes[index].VisibleAfter;
            errors[index] = envelopes[index].LastError!;
        }

        var batchSql = $"""
                       UPDATE {_tableName} AS inbox
                       SET
                           status = @failed_status,
                           visible_after = batch.visible_after,
                           lease_owner = NULL,
                           lease_expires_at = NULL,
                           last_error = batch.last_error
                       FROM unnest(@message_ids, @lease_owners, @visible_after, @last_errors)
                           AS batch(message_id, lease_owner, visible_after, last_error)
                       WHERE inbox.message_id = batch.message_id
                           AND inbox.status = @in_flight_status
                           AND inbox.lease_owner = batch.lease_owner;
                       """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("failed_status", (int)InboxStatus.Failed);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int)InboxStatus.Processing);
        batchCommand.Parameters.AddWithValue("message_ids", ids);
        batchCommand.Parameters.AddWithValue("lease_owners", owners);
        AddVisibleAfterArrayParameter(batchCommand, visibleAfter);
        batchCommand.Parameters.AddWithValue("last_errors", errors);
        await ExecuteTerminalUpdateAsync(batchCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists dead-letter status for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The dead-lettered envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private async Task PersistDeadLetteredAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 1)
        {
            var envelope = envelopes[0];
            var sql = $"""
                      UPDATE {_tableName}
                      SET
                          status = @dead_lettered_status,
                          lease_owner = NULL,
                          lease_expires_at = NULL,
                          last_error = @last_error
                      WHERE message_id = @message_id
                          AND status = @in_flight_status
                          AND lease_owner = @owner;
                      """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("dead_lettered_status", (int)InboxStatus.DeadLettered);
            command.Parameters.AddWithValue("in_flight_status", (int)InboxStatus.Processing);
            command.Parameters.AddWithValue("last_error", envelope.LastError!);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            await ExecuteTerminalUpdateAsync(command, cancellationToken).ConfigureAwait(false);
            return;
        }

        var ids = new Guid[envelopes.Count];
        var owners = new string[envelopes.Count];
        var reasons = new string[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            ids[index] = envelopes[index].Id;
            owners[index] = envelopes[index].LeaseOwner!;
            reasons[index] = envelopes[index].LastError!;
        }

        var batchSql = $"""
                       UPDATE {_tableName} AS inbox
                       SET
                           status = @dead_lettered_status,
                           lease_owner = NULL,
                           lease_expires_at = NULL,
                           last_error = batch.last_error
                       FROM unnest(@message_ids, @lease_owners, @last_errors)
                           AS batch(message_id, lease_owner, last_error)
                       WHERE inbox.message_id = batch.message_id
                           AND inbox.status = @in_flight_status
                           AND inbox.lease_owner = batch.lease_owner;
                       """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("dead_lettered_status", (int)InboxStatus.DeadLettered);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int)InboxStatus.Processing);
        batchCommand.Parameters.AddWithValue("message_ids", ids);
        batchCommand.Parameters.AddWithValue("lease_owners", owners);
        batchCommand.Parameters.AddWithValue("last_errors", reasons);
        await ExecuteTerminalUpdateAsync(batchCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes a guarded terminal update and ignores zero-row results when the lease was reclaimed.
    /// </summary>
    /// <param name="command">The update command with terminal guard parameters already bound.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private static async Task ExecuteTerminalUpdateAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return;
        }

        if (messageIds.Count == 1)
        {
            var singleSql = $"""
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

            await using var singleCommand = CreateCommand(singleSql);
            singleCommand.Parameters.AddWithValue("pending_status", (int)InboxStatus.Pending);
            singleCommand.Parameters.AddWithValue("dead_lettered_status", (int)InboxStatus.DeadLettered);
            singleCommand.Parameters.AddWithValue("message_id", messageIds[0]);
            await singleCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var sql = $"""
                  UPDATE {_tableName}
                  SET
                      status = @pending_status,
                      visible_after = NULL,
                      attempt_count = 0,
                      lease_owner = NULL,
                      lease_expires_at = NULL,
                      last_error = NULL
                  WHERE message_id = ANY(@message_ids) AND status = @dead_lettered_status;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)InboxStatus.Pending);
        command.Parameters.AddWithValue("dead_lettered_status", (int)InboxStatus.DeadLettered);
        command.Parameters.AddWithValue("message_ids", messageIds);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  DELETE FROM {_tableName}
                  WHERE status = @completed_status
                      AND COALESCE(completed_at, created_at) < @older_than;
                  """;

        await using var command = CreateCommand(sql);
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

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<InboxStatus, int>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[(InboxStatus)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    /// <inheritdoc />
    public async Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pageRequest);
        ValidatePageSize(pageRequest.PageSize);

        var sql = $"""
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
                      trace_context::text,
                      completed_at
                  FROM {_tableName}
                  WHERE (@status_filter OR status = ANY(@statuses))
                      AND (@contract_name IS NULL OR contract_name = @contract_name)
                      AND (@correlation_id IS NULL OR correlation_id = @correlation_id)
                      AND (@causation_id IS NULL OR causation_id = @causation_id)
                      AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
                      AND (@created_after IS NULL OR created_at >= @created_after)
                      AND (@created_before IS NULL OR created_at <= @created_before)
                      AND (
                          @cursor_created_at IS NULL
                          OR (created_at, message_id) > (@cursor_created_at, @cursor_id)
                      )
                  ORDER BY created_at ASC, message_id ASC
                  LIMIT @page_size;
                  """;

        await using var command = CreateCommand(sql);
        AddFilterParameters(command, filter);
        AddCursorParameters(command, pageRequest.Cursor);
        command.Parameters.AddWithValue("page_size", pageRequest.PageSize + 1);

        var envelopes = await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
        return BuildPage(envelopes, pageRequest.PageSize);
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var sql = $"""
                  DELETE FROM {_tableName}
                  WHERE (@status_filter OR status = ANY(@statuses))
                      AND (@contract_name IS NULL OR contract_name = @contract_name)
                      AND (@correlation_id IS NULL OR correlation_id = @correlation_id)
                      AND (@causation_id IS NULL OR causation_id = @causation_id)
                      AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
                      AND (@created_after IS NULL OR created_at >= @created_after)
                      AND (@created_before IS NULL OR created_at <= @created_before);
                  """;

        await using var command = CreateCommand(sql);
        AddFilterParameters(command, filter);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        await using var command = CreateCommand();
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
                       trace_context::text,
                       completed_at
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
                       trace_context::text,
                       completed_at
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

    /// <summary>
    ///     Adds a nullable timestamptz array parameter for batch failure updates.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="visibleAfter">The per-message next visibility timestamps.</param>
    private static void AddVisibleAfterArrayParameter(NpgsqlCommand command, DateTimeOffset?[] visibleAfter)
    {
        var parameter = command.Parameters.Add("visible_after", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz);
        parameter.Value = visibleAfter;
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command with the database types expected by the inbox table.
    /// </summary>
    /// <param name="command">The command that will insert an inbox row.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, InboxEnvelope envelope)
    {
        AddEnvelopeParameters(command, envelope, parameterSuffix: null);
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command for a single-row or batched insert.
    /// </summary>
    /// <param name="command">The command that will insert one or more inbox rows.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    /// <param name="parameterSuffix">
    ///     The optional batch index appended to parameter names; pass <see langword="null" /> for single-row inserts.
    /// </param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, InboxEnvelope envelope, int? parameterSuffix)
    {
        var suffix = parameterSuffix is null ? string.Empty : $"_{parameterSuffix}";

        command.Parameters.AddWithValue($"message_id{suffix}", envelope.Id);
        command.Parameters.AddWithValue($"contract_name{suffix}", envelope.ContractName);
        command.Parameters.AddWithValue($"contract_version{suffix}", envelope.ContractVersion);

        var payloadParameter = command.Parameters.Add($"payload{suffix}", NpgsqlDbType.Jsonb);
        payloadParameter.Value = envelope.Payload;

        command.Parameters.AddWithValue($"created_at{suffix}", envelope.CreatedAt);
        command.Parameters.AddWithValue($"visible_after{suffix}", (object?)envelope.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue($"attempt_count{suffix}", envelope.AttemptCount);
        command.Parameters.AddWithValue($"status{suffix}", (int)envelope.Status);
        command.Parameters.AddWithValue($"idempotency_key{suffix}", (object?)envelope.IdempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue($"lease_owner{suffix}", (object?)envelope.LeaseOwner ?? DBNull.Value);
        command.Parameters.AddWithValue($"lease_expires_at{suffix}", (object?)envelope.LeaseExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue($"last_error{suffix}", (object?)envelope.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue($"correlation_id{suffix}", (object?)envelope.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue($"causation_id{suffix}", (object?)envelope.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue($"tenant_id{suffix}", (object?)envelope.TenantId ?? DBNull.Value);

        var traceContextParameter = command.Parameters.Add($"trace_context{suffix}", NpgsqlDbType.Jsonb);
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
            TraceContext = GetNullableString(reader, 15),
            CompletedAt = GetNullable<DateTimeOffset>(reader, 16)
        };
    }

    /// <summary>
    ///     Resolves the completed timestamp stored for a terminal persist operation.
    /// </summary>
    /// <param name="envelope">The completed envelope being persisted.</param>
    /// <returns>The UTC timestamp written to <c>completed_at</c>.</returns>
    private static DateTimeOffset ResolveCompletedAt(InboxEnvelope envelope) =>
        envelope.CompletedAt ?? DateTimeOffset.UtcNow;

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
    ///     Adds shared filter parameters to a PostgreSQL command.
    /// </summary>
    /// <param name="command">The command receiving filter parameters.</param>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    private static void AddFilterParameters(NpgsqlCommand command, InboxMessageFilter filter)
    {
        var hasStatusFilter = filter.Statuses is { Count: > 0 };
        var statuses = hasStatusFilter
            ? filter.Statuses!.Select(status => (int)status).ToArray()
            : Array.Empty<int>();

        command.Parameters.AddWithValue("status_filter", !hasStatusFilter);
        command.Parameters.AddWithValue("statuses", statuses);
        AddNullableTextParameter(command, "contract_name", filter.ContractName);
        AddNullableTextParameter(command, "correlation_id", filter.CorrelationId);
        AddNullableTextParameter(command, "causation_id", filter.CausationId);
        AddNullableTextParameter(command, "tenant_id", filter.TenantId);
        AddNullableTimestampParameter(command, "created_after", filter.CreatedAfter);
        AddNullableTimestampParameter(command, "created_before", filter.CreatedBefore);
    }

    /// <summary>
    ///     Adds keyset cursor parameters to a PostgreSQL command.
    /// </summary>
    /// <param name="command">The command receiving cursor parameters.</param>
    /// <param name="cursor">The opaque cursor from a previous page.</param>
    private static void AddCursorParameters(NpgsqlCommand command, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            AddNullableTimestampParameter(command, "cursor_created_at", null);
            command.Parameters.Add(new NpgsqlParameter("cursor_id", NpgsqlDbType.Uuid) { Value = DBNull.Value });
            return;
        }

        if (!InboxMessagePageCursor.TryDecode(cursor, out var createdAt, out var messageId))
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor));
        }

        AddNullableTimestampParameter(command, "cursor_created_at", createdAt);
        command.Parameters.AddWithValue("cursor_id", messageId);
    }

    /// <summary>
    ///     Builds a page result from one over-fetched query batch.
    /// </summary>
    /// <param name="envelopes">The ordered envelopes including one optional lookahead row.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <returns>The page returned to callers.</returns>
    private static InboxMessagePage BuildPage(IReadOnlyList<InboxEnvelope> envelopes, int pageSize)
    {
        var hasMore = envelopes.Count > pageSize;
        var items = hasMore ? envelopes.Take(pageSize).ToList() : envelopes;
        var nextCursor = hasMore
            ? InboxMessagePageCursor.Encode(items[^1].CreatedAt, items[^1].Id)
            : null;

        return new InboxMessagePage(items, hasMore, nextCursor);
    }

    /// <summary>
    ///     Validates that the requested page size is positive.
    /// </summary>
    /// <param name="pageSize">The requested page size.</param>
    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be greater than zero.");
        }
    }

    /// <summary>
    ///     Adds a nullable text parameter with an explicit PostgreSQL type so null checks compile in SQL.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The optional text value.</param>
    private static void AddNullableTextParameter(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value
        });
    }

    /// <summary>
    ///     Adds a nullable timestamp parameter with an explicit PostgreSQL type so null checks compile in SQL.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The optional timestamp value.</param>
    private static void AddNullableTimestampParameter(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = value.HasValue ? value.Value.UtcDateTime : DBNull.Value
        });
    }
}