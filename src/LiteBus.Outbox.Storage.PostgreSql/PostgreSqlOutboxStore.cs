using System;
using System.Collections.Generic;
using System.Linq;
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
///         caller's PostgreSQL transaction. Without that overload, <see cref="IOutbox.EnqueueAsync" /> commits in a separate
///         transaction from manual SQL or ADO.NET work.
///     </para>
/// </remarks>
public sealed class PostgreSqlOutboxStore :
    IOutboxStore,
    IOutboxLeaseStore,
    IOutboxStateWriter,
    IOutboxDeadLetterStore,
    IOutboxRetentionStore,
    IOutboxDiagnosticsStore,
    IOutboxMessageQuery,
    IOutboxPurgeStore
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
                      trace_context::text,
                      published_at;
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
                           OR (status = @publishing_status AND lease_expires_at IS NOT NULL AND lease_expires_at <= @now)
                           OR (status = @publishing_status AND lease_expires_at IS NULL AND created_at < @stale_cutoff))
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
                      outbox.idempotency_key,
                      outbox.trace_context::text,
                      outbox.published_at;
                  """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("pending_status", (int)OutboxStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int)OutboxStatus.Failed);
        command.Parameters.AddWithValue("publishing_status", (int)OutboxStatus.Publishing);
        command.Parameters.AddWithValue("now", request.Now);
        command.Parameters.AddWithValue("batch_size", request.BatchSize);
        command.Parameters.AddWithValue("lease_owner", request.LeaseOwner);
        command.Parameters.AddWithValue("lease_expires_at", request.Now.Add(request.LeaseDuration));
        command.Parameters.AddWithValue("stale_cutoff", request.Now.Add(-request.LeaseDuration));

        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PersistAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return;
        }

        List<OutboxEnvelope>? published = null;
        List<OutboxEnvelope>? failed = null;
        List<OutboxEnvelope>? deadLettered = null;

        foreach (var envelope in envelopes)
        {
            switch (envelope.Status)
            {
                case OutboxStatus.Published:
                    (published ??= []).Add(envelope);
                    break;

                case OutboxStatus.Failed:
                    (failed ??= []).Add(envelope);
                    break;

                case OutboxStatus.DeadLettered:
                    (deadLettered ??= []).Add(envelope);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Envelope '{envelope.Id}' has unexpected status '{envelope.Status}' in PersistAsync. " +
                        "Only Published, Failed, and DeadLettered are valid outcomes.");
            }
        }

        await PersistTerminalGroupsAsync(published, failed, deadLettered, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists grouped terminal envelopes inside one PostgreSQL transaction when the store is not caller-bound.
    /// </summary>
    /// <param name="published">The published envelopes to persist, if any.</param>
    /// <param name="failed">The failed envelopes to persist, if any.</param>
    /// <param name="deadLettered">The dead-lettered envelopes to persist, if any.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private async Task PersistTerminalGroupsAsync(
        IReadOnlyList<OutboxEnvelope>? published,
        IReadOnlyList<OutboxEnvelope>? failed,
        IReadOnlyList<OutboxEnvelope>? deadLettered,
        CancellationToken cancellationToken)
    {
        if (_transactionConnection is null || _transaction is null)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var scopedStore = new PostgreSqlOutboxStore(_dataSource, _tableName, connection, transaction);
            await scopedStore.PersistTerminalGroupsAsync(published, failed, deadLettered, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (published is not null)
        {
            await PersistPublishedAsync(published, cancellationToken).ConfigureAwait(false);
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
    ///     Persists published status for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The published envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that represents the asynchronous update.</returns>
    private async Task PersistPublishedAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 1)
        {
            var envelope = envelopes[0];
            var sql = $"""
                      UPDATE {_tableName}
                      SET
                          status = @published_status,
                          lease_owner = NULL,
                          lease_expires_at = NULL,
                          last_error = NULL,
                          published_at = @published_at
                      WHERE message_id = @message_id
                          AND status = @in_flight_status
                          AND lease_owner = @owner;
                      """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("published_status", (int)OutboxStatus.Published);
            command.Parameters.AddWithValue("in_flight_status", (int)OutboxStatus.Publishing);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            command.Parameters.AddWithValue("published_at", ResolvePublishedAt(envelope));
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
                       UPDATE {_tableName} AS outbox
                       SET
                           status = @published_status,
                           lease_owner = NULL,
                           lease_expires_at = NULL,
                           last_error = NULL,
                           published_at = NOW()
                       FROM unnest(@message_ids, @lease_owners) AS batch(message_id, lease_owner)
                       WHERE outbox.message_id = batch.message_id
                           AND outbox.status = @in_flight_status
                           AND outbox.lease_owner = batch.lease_owner;
                       """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("published_status", (int)OutboxStatus.Published);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int)OutboxStatus.Publishing);
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
    private async Task PersistFailedAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken)
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
            command.Parameters.AddWithValue("failed_status", (int)OutboxStatus.Failed);
            command.Parameters.AddWithValue("in_flight_status", (int)OutboxStatus.Publishing);
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
                       UPDATE {_tableName} AS outbox
                       SET
                           status = @failed_status,
                           visible_after = batch.visible_after,
                           lease_owner = NULL,
                           lease_expires_at = NULL,
                           last_error = batch.last_error
                       FROM unnest(@message_ids, @lease_owners, @visible_after, @last_errors)
                           AS batch(message_id, lease_owner, visible_after, last_error)
                       WHERE outbox.message_id = batch.message_id
                           AND outbox.status = @in_flight_status
                           AND outbox.lease_owner = batch.lease_owner;
                       """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("failed_status", (int)OutboxStatus.Failed);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int)OutboxStatus.Publishing);
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
    private async Task PersistDeadLetteredAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken)
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
            command.Parameters.AddWithValue("dead_lettered_status", (int)OutboxStatus.DeadLettered);
            command.Parameters.AddWithValue("in_flight_status", (int)OutboxStatus.Publishing);
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
                       UPDATE {_tableName} AS outbox
                       SET
                           status = @dead_lettered_status,
                           lease_owner = NULL,
                           lease_expires_at = NULL,
                           last_error = batch.last_error
                       FROM unnest(@message_ids, @lease_owners, @last_errors)
                           AS batch(message_id, lease_owner, last_error)
                       WHERE outbox.message_id = batch.message_id
                           AND outbox.status = @in_flight_status
                           AND outbox.lease_owner = batch.lease_owner;
                       """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("dead_lettered_status", (int)OutboxStatus.DeadLettered);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int)OutboxStatus.Publishing);
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
            singleCommand.Parameters.AddWithValue("pending_status", (int)OutboxStatus.Pending);
            singleCommand.Parameters.AddWithValue("dead_lettered_status", (int)OutboxStatus.DeadLettered);
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
        command.Parameters.AddWithValue("pending_status", (int)OutboxStatus.Pending);
        command.Parameters.AddWithValue("dead_lettered_status", (int)OutboxStatus.DeadLettered);
        command.Parameters.AddWithValue("message_ids", messageIds);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var sql = $"""
                  DELETE FROM {_tableName}
                  WHERE status = @published_status
                      AND COALESCE(published_at, created_at) < @older_than;
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

    /// <inheritdoc />
    public async Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
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
                      trace_context::text,
                      published_at
                  FROM {_tableName}
                  WHERE (@status_filter OR status = ANY(@statuses))
                      AND (@contract_name IS NULL OR contract_name = @contract_name)
                      AND (@topic IS NULL OR topic = @topic)
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
    public async Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var sql = $"""
                  DELETE FROM {_tableName}
                  WHERE (@status_filter OR status = ANY(@statuses))
                      AND (@contract_name IS NULL OR contract_name = @contract_name)
                      AND (@topic IS NULL OR topic = @topic)
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
    /// <param name="idempotencyKey">The optional idempotency key from the attempted insert.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The existing stored envelope that should be returned to the writer.</returns>
    private async Task<OutboxEnvelope> FindExistingAsync(
        Guid messageId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
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
                       trace_context::text,
                       published_at
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
                       trace_context::text,
                       published_at
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
            TraceContext = GetNullableString(reader, 16),
            PublishedAt = GetNullable<DateTimeOffset>(reader, 17)
        };
    }

    /// <summary>
    ///     Resolves the published timestamp stored for a terminal persist operation.
    /// </summary>
    /// <param name="envelope">The published envelope being persisted.</param>
    /// <returns>The UTC timestamp written to <c>published_at</c>.</returns>
    private static DateTimeOffset ResolvePublishedAt(OutboxEnvelope envelope) =>
        envelope.PublishedAt ?? DateTimeOffset.UtcNow;

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
    ///     Adds shared filter parameters to a PostgreSQL command.
    /// </summary>
    /// <param name="command">The command receiving filter parameters.</param>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    private static void AddFilterParameters(NpgsqlCommand command, OutboxMessageFilter filter)
    {
        var hasStatusFilter = filter.Statuses is { Count: > 0 };
        var statuses = hasStatusFilter
            ? filter.Statuses!.Select(status => (int)status).ToArray()
            : Array.Empty<int>();

        command.Parameters.AddWithValue("status_filter", !hasStatusFilter);
        command.Parameters.AddWithValue("statuses", statuses);
        command.Parameters.AddWithValue("contract_name", (object?)filter.ContractName ?? DBNull.Value);
        command.Parameters.AddWithValue("topic", (object?)filter.Topic ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)filter.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causation_id", (object?)filter.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("tenant_id", (object?)filter.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("created_after", (object?)filter.CreatedAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("created_before", (object?)filter.CreatedBefore ?? DBNull.Value);
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
            command.Parameters.AddWithValue("cursor_created_at", DBNull.Value);
            command.Parameters.AddWithValue("cursor_id", DBNull.Value);
            return;
        }

        if (!OutboxMessagePageCursor.TryDecode(cursor, out var createdAt, out var messageId))
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor));
        }

        command.Parameters.AddWithValue("cursor_created_at", createdAt);
        command.Parameters.AddWithValue("cursor_id", messageId);
    }

    /// <summary>
    ///     Builds a page result from one over-fetched query batch.
    /// </summary>
    /// <param name="envelopes">The ordered envelopes including one optional lookahead row.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <returns>The page returned to callers.</returns>
    private static OutboxMessagePage BuildPage(IReadOnlyList<OutboxEnvelope> envelopes, int pageSize)
    {
        var hasMore = envelopes.Count > pageSize;
        var items = hasMore ? envelopes.Take(pageSize).ToList() : envelopes;
        var nextCursor = hasMore
            ? OutboxMessagePageCursor.Encode(items[^1].CreatedAt, items[^1].Id)
            : null;

        return new OutboxMessagePage(items, hasMore, nextCursor);
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
}