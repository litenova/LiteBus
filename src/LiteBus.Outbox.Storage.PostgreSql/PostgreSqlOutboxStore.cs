using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Stores;
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
///         caller's PostgreSQL transaction. Without that overload, <see cref="IOutbox.EnqueueAsync" /> commits in a
///         separate
///         transaction from manual SQL or ADO.NET work.
///     </para>
/// </remarks>
public sealed class PostgreSqlOutboxStore :
    IOutboxStore,
    ITransactionalOutboxStore,
    IOutboxProcessingStore,
    IOutboxOperationsStore
{
    /// <summary>
    ///     The shared SELECT column list used by batch idempotency lookups.
    /// </summary>
    private const string BatchSelectColumns = """
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
                                              """;

    /// <summary>
    ///     The PostgreSQL data source used to open commands against the outbox table.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The store table and metadata options.
    /// </summary>
    private readonly PostgreSqlOutboxStoreOptions _options;

    /// <summary>
    ///     The quoted qualified outbox table name built from store options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     The existing PostgreSQL transaction used for command execution when provided by the caller.
    /// </summary>
    private readonly NpgsqlTransaction? _transaction;

    /// <summary>
    ///     The existing open PostgreSQL connection used when callers provide an external transaction boundary.
    /// </summary>
    private readonly NpgsqlConnection? _transactionConnection;

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
        _options = options;
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
        _options = new PostgreSqlOutboxStoreOptions();
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _transactionConnection = transactionConnection;
        _transaction = transaction;
    }

    /// <inheritdoc />
    public async Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return new RequeueResult(0, 0);
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
            singleCommand.Parameters.AddWithValue("pending_status", (int) OutboxStatus.Pending);
            singleCommand.Parameters.AddWithValue("dead_lettered_status", (int) OutboxStatus.DeadLettered);
            singleCommand.Parameters.AddWithValue("message_id", messageIds[0]);
            var requeued = await singleCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new RequeueResult(1, requeued);
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
        command.Parameters.AddWithValue("pending_status", (int) OutboxStatus.Pending);
        command.Parameters.AddWithValue("dead_lettered_status", (int) OutboxStatus.DeadLettered);
        PostgreSqlParameterExtensions.AddUuidArrayParameter(command, "message_ids", messageIds.ToArray());
        var batchRequeued = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new RequeueResult(messageIds.Count, batchRequeued);
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
        command.Parameters.AddWithValue("published_status", (int) OutboxStatus.Published);
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
            counts[(OutboxStatus) reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    /// <inheritdoc />
    public async Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var recordedVersion = await PostgreSqlSchemaVersionStore.GetVersionAsync(
                connection,
                _options,
                PostgreSqlSchemaComponents.Outbox,
                _options.SchemaName,
                _options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        var recorded = recordedVersion == 0
            ? PostgreSqlOutboxSchema.CurrentSchemaVersion
            : recordedVersion;

        return new StoreSchemaInfo(
            PostgreSqlSchemaComponents.Outbox,
            PostgreSqlOutboxSchema.CurrentSchemaVersion,
            recorded,
            _options.SchemaName,
            _options.TableName);
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
                       AND (@message_id IS NULL OR message_id = @message_id)
                       AND (@message_ids IS NULL OR message_id = ANY(@message_ids))
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
                       AND (@message_id IS NULL OR message_id = @message_id)
                       AND (@message_ids IS NULL OR message_id = ANY(@message_ids))
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(OutboxLeaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sql = $"""
                   WITH candidates AS (
                       SELECT message_id
                       FROM {_tableName}
                       WHERE
                           (@tenant_id IS NULL OR tenant_id = @tenant_id)
                           AND ((status IN (@pending_status, @failed_status) AND (visible_after IS NULL OR visible_after <= @now))
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
                       attempt_count = outbox.attempt_count + 1,
                       last_attempted_at = @now
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
        command.Parameters.AddWithValue("pending_status", (int) OutboxStatus.Pending);
        command.Parameters.AddWithValue("failed_status", (int) OutboxStatus.Failed);
        command.Parameters.AddWithValue("publishing_status", (int) OutboxStatus.Publishing);
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
        LeaseRenewalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);

        var sql = $"""
                   UPDATE {_tableName}
                   SET lease_expires_at = @lease_expires_at
                   WHERE message_id = @message_id
                       AND status = @publishing_status
                       AND lease_owner = @lease_owner;
                   """;

        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("lease_expires_at", request.ExpiresAt);
        command.Parameters.AddWithValue("message_id", request.MessageId);
        command.Parameters.AddWithValue("publishing_status", (int) OutboxStatus.Publishing);
        command.Parameters.AddWithValue("lease_owner", request.LeaseOwner);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<PersistResult> PersistAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return PersistResult.Empty;
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

        return await PersistTerminalGroupsAsync(published, failed, deadLettered, envelopes, cancellationToken)
            .ConfigureAwait(false);
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

        if (storedEnvelope is not null)
        {
            return storedEnvelope;
        }

        var existing = await FindExistingAsync(envelope.Id, envelope.IdempotencyKey, cancellationToken).ConfigureAwait(false);

        if (existing is not null &&
            envelope.IdempotencyConflictMode == Messaging.Abstractions.DurableMessaging.IdempotencyConflictMode.Strict &&
            existing.Id != envelope.Id)
        {
            throw new Messaging.Abstractions.DurableMessaging.IdempotencyConflictException(
                $"An outbox message with idempotency key '{envelope.IdempotencyKey}' or message id '{envelope.Id}' already exists.");
        }

        return existing ?? envelope;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> AddBatchAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Array.Empty<OutboxEnvelope>();
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
                $"@topic_{index}, @created_at_{index}, @visible_after_{index}, @status_{index}, @attempt_count_{index}, " +
                $"@lease_owner_{index}, @lease_expires_at_{index}, @last_error_{index}, @correlation_id_{index}, " +
                $"@causation_id_{index}, @tenant_id_{index}, @idempotency_key_{index}, @trace_context_{index})");
        }

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
                   VALUES {valueClauses}
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

        for (var index = 0; index < envelopes.Count; index++)
        {
            AddEnvelopeParameters(command, envelopes[index], index);
        }

        var inserted = await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
        var insertedById = inserted.ToDictionary(envelope => envelope.Id);
        var stored = new OutboxEnvelope[envelopes.Count];
        var missingKeys = new List<PostgreSqlBatchIdempotencyLookup.LookupKey>();

        for (var index = 0; index < envelopes.Count; index++)
        {
            var envelope = envelopes[index];

            if (insertedById.TryGetValue(envelope.Id, out var accepted))
            {
                stored[index] = accepted;
                continue;
            }

            missingKeys.Add(new PostgreSqlBatchIdempotencyLookup.LookupKey(envelope.Id, envelope.IdempotencyKey));
        }

        if (missingKeys.Count > 0)
        {
            var resolved = await PostgreSqlBatchIdempotencyLookup.ResolveAsync(
                    CreateCommand,
                    _tableName,
                    BatchSelectColumns,
                    missingKeys,
                    ReadEnvelope,
                    envelope => envelope.Id,
                    envelope => envelope.IdempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);

            for (var index = 0; index < envelopes.Count; index++)
            {
                if (stored[index] is not null)
                {
                    continue;
                }

                var envelope = envelopes[index];

                stored[index] = resolved.TryGetValue(envelope.Id, out var existing)
                    ? existing
                    : await FindExistingAsync(envelope.Id, envelope.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            }
        }

        return stored;
    }

    /// <summary>
    ///     Returns a store that executes commands on an existing PostgreSQL connection and transaction.
    /// </summary>
    /// <param name="connection">The existing open connection owned by the caller.</param>
    /// <param name="transaction">The transaction that should contain outbox writes.</param>
    /// <returns>A store instance bound to the supplied connection and transaction.</returns>
    public ITransactionalOutboxStore UseExistingConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException("The supplied transaction must belong to the supplied connection.", nameof(transaction));
        }

        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The supplied connection must already be open.");
        }

        return new PostgreSqlOutboxStore(_dataSource, _tableName, connection, transaction);
    }

    /// <summary>
    ///     Persists grouped terminal envelopes inside one PostgreSQL transaction when the store is not caller-bound.
    /// </summary>
    /// <param name="published">The published envelopes to persist, if any.</param>
    /// <param name="failed">The failed envelopes to persist, if any.</param>
    /// <param name="deadLettered">The dead-lettered envelopes to persist, if any.</param>
    /// <param name="requestedEnvelopes">The original persist request used to preserve outcome order.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that returns one outcome per requested envelope.</returns>
    private async Task<PersistResult> PersistTerminalGroupsAsync(
        IReadOnlyList<OutboxEnvelope>? published,
        IReadOnlyList<OutboxEnvelope>? failed,
        IReadOnlyList<OutboxEnvelope>? deadLettered,
        IReadOnlyList<OutboxEnvelope> requestedEnvelopes,
        CancellationToken cancellationToken)
    {
        if (_transactionConnection is null || _transaction is null)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var scopedStore = new PostgreSqlOutboxStore(_dataSource, _tableName, connection, transaction);

            var result = await scopedStore.PersistTerminalGroupsAsync(
                    published,
                    failed,
                    deadLettered,
                    requestedEnvelopes,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        var persistedMessageIds = new HashSet<Guid>();

        if (published is not null)
        {
            persistedMessageIds.UnionWith(await PersistPublishedAsync(published, cancellationToken).ConfigureAwait(false));
        }

        if (failed is not null)
        {
            persistedMessageIds.UnionWith(await PersistFailedAsync(failed, cancellationToken).ConfigureAwait(false));
        }

        if (deadLettered is not null)
        {
            persistedMessageIds.UnionWith(
                await PersistDeadLetteredAsync(deadLettered, cancellationToken).ConfigureAwait(false));
        }

        var messageIds = requestedEnvelopes.Select(envelope => envelope.Id).ToArray();
        return PersistResult.FromMessageIds(messageIds, persistedMessageIds);
    }

    /// <summary>
    ///     Persists published status for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The published envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that returns the message identifiers updated under the lease guard.</returns>
    private async Task<HashSet<Guid>> PersistPublishedAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
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
                           AND lease_owner = @owner
                       RETURNING message_id;
                       """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("published_status", (int) OutboxStatus.Published);
            command.Parameters.AddWithValue("in_flight_status", (int) OutboxStatus.Publishing);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            command.Parameters.AddWithValue("published_at", ResolvePublishedAt(envelope));
            return await ExecuteTerminalUpdateWithReturningAsync(command, cancellationToken).ConfigureAwait(false);
        }

        var ids = new Guid[envelopes.Count];
        var owners = new string[envelopes.Count];
        var publishedAt = new DateTimeOffset[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            ids[index] = envelopes[index].Id;
            owners[index] = envelopes[index].LeaseOwner!;
            publishedAt[index] = ResolvePublishedAt(envelopes[index]);
        }

        var batchSql = $"""
                        UPDATE {_tableName} AS outbox
                        SET
                            status = @published_status,
                            lease_owner = NULL,
                            lease_expires_at = NULL,
                            last_error = NULL,
                            published_at = batch.published_at
                        FROM unnest(@message_ids, @lease_owners, @published_at)
                            AS batch(message_id, lease_owner, published_at)
                        WHERE outbox.message_id = batch.message_id
                            AND outbox.status = @in_flight_status
                            AND outbox.lease_owner = batch.lease_owner
                        RETURNING outbox.message_id;
                        """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("published_status", (int) OutboxStatus.Published);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int) OutboxStatus.Publishing);
        batchCommand.Parameters.AddWithValue("message_ids", ids);
        batchCommand.Parameters.AddWithValue("lease_owners", owners);
        AddTimestampArrayParameter(batchCommand, "published_at", publishedAt);
        return await ExecuteTerminalUpdateWithReturningAsync(batchCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists failed status and retry metadata for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The failed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that returns the message identifiers updated under the lease guard.</returns>
    private async Task<HashSet<Guid>> PersistFailedAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
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
                           AND lease_owner = @owner
                       RETURNING message_id;
                       """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("failed_status", (int) OutboxStatus.Failed);
            command.Parameters.AddWithValue("in_flight_status", (int) OutboxStatus.Publishing);
            command.Parameters.AddWithValue("visible_after", (object?) envelope.VisibleAfter ?? DBNull.Value);
            command.Parameters.AddWithValue("last_error", envelope.LastError!);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            return await ExecuteTerminalUpdateWithReturningAsync(command, cancellationToken).ConfigureAwait(false);
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
                            AND outbox.lease_owner = batch.lease_owner
                        RETURNING outbox.message_id;
                        """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("failed_status", (int) OutboxStatus.Failed);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int) OutboxStatus.Publishing);
        batchCommand.Parameters.AddWithValue("message_ids", ids);
        batchCommand.Parameters.AddWithValue("lease_owners", owners);
        AddVisibleAfterArrayParameter(batchCommand, visibleAfter);
        batchCommand.Parameters.AddWithValue("last_errors", errors);
        return await ExecuteTerminalUpdateWithReturningAsync(batchCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists dead-letter status for one or more envelopes.
    /// </summary>
    /// <param name="envelopes">The dead-lettered envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>A task that returns the message identifiers updated under the lease guard.</returns>
    private async Task<HashSet<Guid>> PersistDeadLetteredAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
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
                           AND lease_owner = @owner
                       RETURNING message_id;
                       """;

            await using var command = CreateCommand(sql);
            command.Parameters.AddWithValue("dead_lettered_status", (int) OutboxStatus.DeadLettered);
            command.Parameters.AddWithValue("in_flight_status", (int) OutboxStatus.Publishing);
            command.Parameters.AddWithValue("last_error", envelope.LastError!);
            command.Parameters.AddWithValue("message_id", envelope.Id);
            command.Parameters.AddWithValue("owner", envelope.LeaseOwner!);
            return await ExecuteTerminalUpdateWithReturningAsync(command, cancellationToken).ConfigureAwait(false);
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
                            AND outbox.lease_owner = batch.lease_owner
                        RETURNING outbox.message_id;
                        """;

        await using var batchCommand = CreateCommand(batchSql);
        batchCommand.Parameters.AddWithValue("dead_lettered_status", (int) OutboxStatus.DeadLettered);
        batchCommand.Parameters.AddWithValue("in_flight_status", (int) OutboxStatus.Publishing);
        batchCommand.Parameters.AddWithValue("message_ids", ids);
        batchCommand.Parameters.AddWithValue("lease_owners", owners);
        batchCommand.Parameters.AddWithValue("last_errors", reasons);
        return await ExecuteTerminalUpdateWithReturningAsync(batchCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes a guarded terminal update and returns the message identifiers that matched the lease guard.
    /// </summary>
    /// <param name="command">The update command with terminal guard parameters already bound.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated by the command.</returns>
    private static async Task<HashSet<Guid>> ExecuteTerminalUpdateWithReturningAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var persistedMessageIds = new HashSet<Guid>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            persistedMessageIds.Add(reader.GetGuid(0));
        }

        return persistedMessageIds;
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

        return await ReadSingleOrDefaultAsync(command, cancellationToken).ConfigureAwait(false) ??
               throw new InvalidOperationException("The outbox insert was skipped but the existing message could not be found.");
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command with the database types expected by the outbox table.
    /// </summary>
    /// <param name="command">The command that will insert an outbox row.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, OutboxEnvelope envelope)
    {
        AddEnvelopeParameters(command, envelope, null);
    }

    /// <summary>
    ///     Adds envelope values to an Npgsql command for a single-row or batched insert.
    /// </summary>
    /// <param name="command">The command that will insert one or more outbox rows.</param>
    /// <param name="envelope">The envelope being inserted.</param>
    /// <param name="parameterSuffix">
    ///     The optional batch index appended to parameter names; pass <see langword="null" /> for single-row inserts.
    /// </param>
    private static void AddEnvelopeParameters(NpgsqlCommand command, OutboxEnvelope envelope, int? parameterSuffix)
    {
        var suffix = parameterSuffix is null ? string.Empty : $"_{parameterSuffix}";

        command.Parameters.AddWithValue($"message_id{suffix}", envelope.Id);
        command.Parameters.AddWithValue($"contract_name{suffix}", envelope.ContractName);
        command.Parameters.AddWithValue($"contract_version{suffix}", envelope.ContractVersion);

        var payloadParameter = command.Parameters.Add($"payload{suffix}", NpgsqlDbType.Jsonb);
        payloadParameter.Value = envelope.Payload;

        command.Parameters.AddWithValue($"topic{suffix}", (object?) envelope.Topic ?? DBNull.Value);
        command.Parameters.AddWithValue($"created_at{suffix}", envelope.CreatedAt);
        command.Parameters.AddWithValue($"visible_after{suffix}", (object?) envelope.VisibleAfter ?? DBNull.Value);
        command.Parameters.AddWithValue($"status{suffix}", (int) envelope.Status);
        command.Parameters.AddWithValue($"attempt_count{suffix}", envelope.AttemptCount);
        command.Parameters.AddWithValue($"lease_owner{suffix}", (object?) envelope.LeaseOwner ?? DBNull.Value);
        command.Parameters.AddWithValue($"lease_expires_at{suffix}", (object?) envelope.LeaseExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue($"last_error{suffix}", (object?) envelope.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue($"correlation_id{suffix}", (object?) envelope.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue($"causation_id{suffix}", (object?) envelope.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue($"tenant_id{suffix}", (object?) envelope.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue($"idempotency_key{suffix}", (object?) envelope.IdempotencyKey ?? DBNull.Value);

        var traceContextParameter = command.Parameters.Add($"trace_context{suffix}", NpgsqlDbType.Jsonb);
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
            Status = (OutboxStatus) reader.GetInt32(7),
            AttemptCount = reader.GetInt32(8),
            LeaseOwner = GetNullableString(reader, 9),
            LeaseExpiresAt = GetNullable<DateTimeOffset>(reader, 10),
            LastError = GetNullableString(reader, 11),
            CorrelationId = GetNullableString(reader, 12),
            CausationId = GetNullableString(reader, 13),
            TenantId = GetNullableString(reader, 14),
            IdempotencyKey = GetNullableString(reader, 15),
            TraceContext = NormalizeJsonText(GetNullableString(reader, 16)),
            PublishedAt = GetNullable<DateTimeOffset>(reader, 17)
        };
    }

    /// <summary>
    ///     Normalizes JSON text read from <c>jsonb</c> columns to a compact round-trip form.
    /// </summary>
    /// <param name="json">The JSON text returned by PostgreSQL.</param>
    /// <returns>The normalized JSON text, or <see langword="null" /> when the input is empty.</returns>
    private static string? NormalizeJsonText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    /// <summary>
    ///     Resolves the published timestamp stored for a terminal persist operation.
    /// </summary>
    /// <param name="envelope">The published envelope being persisted.</param>
    /// <returns>The UTC timestamp written to <c>published_at</c>.</returns>
    private static DateTimeOffset ResolvePublishedAt(OutboxEnvelope envelope)
    {
        return envelope.PublishedAt ?? DateTimeOffset.UtcNow;
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
    ///     Adds a timestamptz array parameter for batch terminal timestamp updates.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="timestamps">The per-message terminal timestamps.</param>
    private static void AddTimestampArrayParameter(NpgsqlCommand command, string name, DateTimeOffset[] timestamps)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Array | NpgsqlDbType.TimestampTz);
        parameter.Value = timestamps;
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
            ? filter.Statuses!.Select(status => (int) status).ToArray()
            : Array.Empty<int>();

        command.Parameters.AddWithValue("status_filter", !hasStatusFilter);
        command.Parameters.AddWithValue("statuses", statuses);

        command.Parameters.Add(new NpgsqlParameter("message_id", NpgsqlDbType.Uuid)
        {
            Value = filter.MessageId is null ? DBNull.Value : filter.MessageId.Value
        });

        command.Parameters.Add(new NpgsqlParameter("message_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = filter.MessageIds is { Count: > 0 } ? filter.MessageIds.ToArray() : DBNull.Value
        });

        AddNullableTextParameter(command, "contract_name", filter.ContractName);
        AddNullableTextParameter(command, "topic", filter.Topic);
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

        if (!OutboxMessagePageCursor.TryDecode(cursor, out var createdAt, out var messageId))
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
            Value = (object?) value ?? DBNull.Value
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