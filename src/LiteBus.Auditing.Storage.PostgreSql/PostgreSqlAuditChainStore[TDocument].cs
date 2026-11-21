using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Stores hash-chained audit documents in PostgreSQL tables: staged and linked documents keyed by document
///     identity, segment heads with conditional versions, and checkpoints with publish state.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         The stored document type must carry a readable <c>Id</c> property, matched case-insensitively, so staged
///         documents can be ordered, matched, and erased. The constructor fails when it does not. The identity is
///         stored as text converted with the invariant culture. One row exists per identity: staging the same
///         document twice overwrites the staged row rather than duplicating it, so a redelivered record overwrites
///         rather than duplicates.
///     </para>
///     <para>
///         The store holds no connection, transaction or scoped service, so the auditing module registers it as a
///         singleton. Staging joins the business unit of work through the scoped
///         <see cref="IPostgreSqlTransactionProvider" /> the caller passes: the audit trail passes its scope's
///         provider, so a staged success commits or rolls back with the handler change.
///         <see cref="StageOutOfBandAsync" />, appends, reads, erasure, expiry and the checkpoint methods run on the
///         store's own connections: a record that must survive a rollback cannot ride the business transaction, and
///         the linker and the checkpoint lifecycle run after commit, outside business transactions. A caller that
///         needs those operations inside its own transaction binds the store with <see cref="UseExistingConnection" />.
///     </para>
///     <para>
///         Appends serialize on the segment head. The store locks the head row, checks the expected head version
///         explicitly, and relies on the unique segment and sequence constraint as the backstop; either way a lost
///         race reports <see cref="AuditHeadConflictException" /> and the caller retries against the winner.
///     </para>
///     <para>
///         Each statement opens its own pooled connection and no connection is held between statements, so the store
///         works behind PgBouncer in transaction mode and with pooling disabled. Every timestamp the store stamps
///         itself comes from the injected clock; every other timestamp is persisted as the engine passed it.
///     </para>
/// </remarks>
public sealed class PostgreSqlAuditChainStore<TDocument> : IAuditChainStore<TDocument>, ICheckpointSource
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The number of documents one segment page reads before yielding them.
    /// </summary>
    private const int SegmentPageSize = 1000;

    /// <summary>
    ///     The JSON options documents, attributes, and signatures are written and read with.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     The PostgreSQL data source used to open one connection per store operation.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The quoted qualified records table name built from store options at construction time.
    /// </summary>
    private readonly string _recordsTable;

    /// <summary>
    ///     The quoted qualified heads table name built from store options at construction time.
    /// </summary>
    private readonly string _headsTable;

    /// <summary>
    ///     The quoted qualified checkpoints table name built from store options at construction time.
    /// </summary>
    private readonly string _checkpointsTable;

    /// <summary>
    ///     The existing open PostgreSQL connection used when callers provide an external transaction boundary.
    /// </summary>
    private readonly NpgsqlConnection? _transactionConnection;

    /// <summary>
    ///     The existing PostgreSQL transaction used for command execution when provided by the caller.
    /// </summary>
    private readonly NpgsqlTransaction? _transaction;

    /// <summary>
    ///     The clock that stamps staged documents.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The readable identity property of the stored document type.
    /// </summary>
    private readonly PropertyInfo _identityProperty;

    /// <summary>
    ///     The readable tenant property of the stored document type, or <see langword="null" /> when the type
    ///     carries none.
    /// </summary>
    private readonly PropertyInfo? _tenantProperty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditChainStore{TDocument}" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL audit store configuration.</param>
    /// <param name="clock">The clock that stamps staged documents, or <see langword="null" /> for <see cref="TimeProvider.System" />.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TDocument" /> carries no readable <c>Id</c> property.</exception>
    /// <remarks>
    ///     The store reads the data source and the table names from <paramref name="registration" /> rather than
    ///     taking a bare <see cref="NpgsqlDataSource" />, so the auditing module can register the closed store by
    ///     type and the container still resolves the data source this audit module registered, even when other
    ///     PostgreSQL modules in the same composition own theirs.
    /// </remarks>
    public PostgreSqlAuditChainStore(
        PostgreSqlAuditStoreRegistration registration,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var options = registration.Options;

        _dataSource = registration.DataSource;
        _recordsTable = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        _headsTable = PostgreSqlIdentifier.Qualify(options.SchemaName, options.HeadsTableName);
        _checkpointsTable = PostgreSqlIdentifier.Qualify(options.SchemaName, options.CheckpointsTableName);
        _clock = clock ?? TimeProvider.System;
        _identityProperty = ResolveIdentityProperty();
        _tenantProperty = ResolveTenantProperty();
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditChainStore{TDocument}" /> class bound to an
    ///     existing transaction.
    /// </summary>
    /// <param name="source">The store the bound copy inherits its configuration from.</param>
    /// <param name="transactionConnection">The existing open PostgreSQL connection.</param>
    /// <param name="transaction">The existing PostgreSQL transaction.</param>
    private PostgreSqlAuditChainStore(
        PostgreSqlAuditChainStore<TDocument> source,
        NpgsqlConnection transactionConnection,
        NpgsqlTransaction transaction)
    {
        _dataSource = source._dataSource;
        _recordsTable = source._recordsTable;
        _headsTable = source._headsTable;
        _checkpointsTable = source._checkpointsTable;
        _transactionConnection = transactionConnection;
        _transaction = transaction;
        _clock = source._clock;
        _identityProperty = source._identityProperty;
        _tenantProperty = source._tenantProperty;
    }

    /// <summary>
    ///     Returns a store that executes commands on an existing PostgreSQL connection and transaction.
    /// </summary>
    /// <param name="connection">The existing open connection owned by the caller.</param>
    /// <param name="transaction">The transaction that should contain audit writes.</param>
    /// <returns>A store instance bound to the supplied connection and transaction.</returns>
    public PostgreSqlAuditChainStore<TDocument> UseExistingConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)
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

        return new PostgreSqlAuditChainStore<TDocument>(this, connection, transaction);
    }

    /// <inheritdoc />
    public async ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             SELECT segment, tenant_id, attributes::text, opened_at, last_sequence, last_hash,
                 document_count, last_checkpoint_sequence, last_checkpoint_at, expired, version
             FROM {_headsTable}
             WHERE segment = @segment;
             """);
        command.Parameters.AddWithValue("segment", segment);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return MapHead(reader);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The appended documents must belong to <paramref name="segment" /> and carry strictly increasing
    ///     sequences; an empty list moves the head only, which is how the checkpoint lifecycle records a cut. A
    ///     document that matches a staged row by identity is linked in place, so the after-commit linker never
    ///     duplicates it; a document with no staged row is inserted. A document whose identity is already linked,
    ///     or whose staged row belongs to another segment, is rejected, because repositioning a committed record
    ///     would rewrite the chain suffix. A stored head that expired rejects the append, because an expired
    ///     segment accepts no further records. The first append stores the described identity from
    ///     <paramref name="updated" />; later appends keep the stored identity and move the position only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The segment expired, a document belongs to another segment, a document is already linked, or
    ///     <paramref name="updated" /> targets another segment.
    /// </exception>
    public async ValueTask AppendAsync(
        string segment,
        AuditSegmentHead expected,
        IReadOnlyList<TDocument> documents,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(updated);

        if (!string.Equals(segment, updated.Info.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The updated audit head targets segment '{updated.Info.Key}' but the append targets '{segment}'.");
        }

        ThrowIfUnexpectedDocuments(segment, documents);

        using var scope = await OpenScopeAsync(true, null, cancellationToken).ConfigureAwait(false);

        try
        {
            var stored = await ReadHeadForUpdateAsync(scope, segment, cancellationToken).ConfigureAwait(false);

            if (stored is null)
            {
                if (expected.Version != 0)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' has no stored head but the append expected version {expected.Version}.");
                }

                await InsertHeadAsync(scope, updated, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (stored.Expired)
                {
                    throw new InvalidOperationException(
                        $"The audit segment '{segment}' has expired and no longer accepts records.");
                }

                if (stored.Version != expected.Version)
                {
                    throw new AuditHeadConflictException(
                        $"The audit segment '{segment}' changed between read and write: expected head version {expected.Version} but found {stored.Version}.");
                }

                await UpdateHeadAsync(scope, updated, cancellationToken).ConfigureAwait(false);
            }

            await PersistLinkedDocumentsAsync(scope, segment, documents, cancellationToken).ConfigureAwait(false);
            await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (IsHeadWriteState(exception.SqlState))
        {
            // Disposing the scope rolls the owned transaction back; a bound or ambient transaction is left
            // to its owner, which fails its own commit on the poisoned transaction.
            throw new AuditHeadConflictException(
                $"The audit segment '{segment}' changed between read and write: another writer committed first.",
                exception);
        }
    }

    /// <summary>
    ///     Stages an unlinked document, joining the business transaction <paramref name="transactionProvider" />
    ///     reports when it has one.
    /// </summary>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="transactionProvider">
    ///     The scoped provider of the business transaction, or <see langword="null" /> to stage on the store's own
    ///     connection.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     The store is a singleton and holds no transaction, so the caller passes the provider of its own scope.
    ///     Staging the same document twice overwrites the staged row rather than duplicating it, so a redelivered
    ///     record overwrites rather than duplicates. Staging a document whose identity is already linked leaves the
    ///     linked row untouched: the record was delivered before and its chain position stands. Staging creates no
    ///     head: the first append creates the head from the described identity, and the after-commit linker links
    ///     staged records for their known segment right after commit.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    public ValueTask StageAsync(
        string segment,
        TDocument document,
        IPostgreSqlTransactionProvider? transactionProvider,
        CancellationToken cancellationToken)
    {
        return StageCoreAsync(segment, document, transactionProvider, cancellationToken);
    }

    /// <summary>
    ///     Stages an unlinked document on its own connection and commits it immediately, outside the business
    ///     transaction.
    /// </summary>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <remarks>
    ///     The unit of work rolls the business transaction back on a denial, failure, cancellation, or invalid
    ///     message, so staging such a record on that transaction would lose it. This method never joins the ambient
    ///     transaction, so the record survives the rollback it describes and the linker still positions it in commit
    ///     order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    public ValueTask StageOutOfBandAsync(string segment, TDocument document, CancellationToken cancellationToken)
    {
        return StageCoreAsync(segment, document, null, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Documents stream in the order they staged, oldest first, by the staged row number rather than the staged
    ///     timestamp, so two records staged inside one clock tick still link in commit order. A document staged
    ///     twice is returned once, because staging overwrites the staged row rather than duplicating it.
    /// </remarks>
    public async IAsyncEnumerable<TDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             SELECT document_key, document::text
             FROM {_recordsTable}
             WHERE segment = @segment
               AND sequence = 0
               AND (@cutoff IS NULL OR staged_at <= @cutoff)
             ORDER BY staged_order ASC;
             """);
        command.Parameters.AddWithValue("segment", segment);
        AddParameter(
            command,
            "cutoff",
            NpgsqlDbType.TimestampTz,
            olderThan.HasValue ? _clock.GetUtcNow() - olderThan.Value : null);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return DeserializeDocument(reader.GetString(1), reader.GetString(0), segment);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Segments stream ordered by segment key.
    /// </remarks>
    public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (olderThan.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(olderThan.Value, TimeSpan.Zero);
        }

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             SELECT DISTINCT segment
             FROM {_recordsTable}
             WHERE sequence = 0
               AND (@cutoff IS NULL OR staged_at <= @cutoff)
             ORDER BY segment ASC;
             """);
        AddParameter(
            command,
            "cutoff",
            NpgsqlDbType.TimestampTz,
            olderThan.HasValue ? _clock.GetUtcNow() - olderThan.Value : null);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.GetString(0);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Documents stream in sequence windows of <see cref="SegmentPageSize" />, so verifying a large segment
    ///     holds one page at a time. The cursor advances past the last sequence returned, which stays correct
    ///     across gaps.
    /// </remarks>
    public async IAsyncEnumerable<TDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentOutOfRangeException.ThrowIfNegative(fromSequence);

        if (toSequence.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(toSequence.Value);

            if (toSequence.Value < fromSequence)
            {
                throw new ArgumentException(
                    "The last sequence to read must not precede the first sequence.",
                    nameof(toSequence));
            }
        }

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        var cursor = Math.Max(fromSequence, 1);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windowEnd = cursor > long.MaxValue - SegmentPageSize
                ? long.MaxValue
                : cursor + SegmentPageSize - 1;
            var pageEnd = toSequence.HasValue ? Math.Min(toSequence.Value, windowEnd) : windowEnd;

            using var command = scope.CreateCommand(
                $"""
                 SELECT document_key, sequence, document::text
                 FROM {_recordsTable}
                 WHERE segment = @segment
                   AND sequence >= @cursor
                   AND sequence <= @pageEnd
                 ORDER BY sequence ASC
                 LIMIT {SegmentPageSize};
                 """);
            command.Parameters.AddWithValue("segment", segment);
            command.Parameters.AddWithValue("cursor", cursor);
            command.Parameters.AddWithValue("pageEnd", pageEnd);

            List<TDocument> page = [];
            var last = 0L;

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                last = reader.GetInt64(1);
                page.Add(DeserializeDocument(reader.GetString(2), reader.GetString(0), segment));
            }

            if (page.Count == 0)
            {
                yield break;
            }

            foreach (var document in page)
            {
                yield return document;
            }

            if (toSequence.HasValue && last >= toSequence.Value)
            {
                yield break;
            }

            if (last == long.MaxValue)
            {
                yield break;
            }

            cursor = last + 1;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The tenant, opened-before, and expiry predicates run in the database, and each named attribute adds
    ///     one JSON containment predicate, so a filter matches only the segments carrying every named value.
    ///     Heads stream ordered by segment key.
    /// </remarks>
    public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);

        var sql = new StringBuilder();
        sql.Append(
            CultureInfo.InvariantCulture,
            $"""
             SELECT segment, tenant_id, attributes::text, opened_at, last_sequence, last_hash,
                 document_count, last_checkpoint_sequence, last_checkpoint_at, expired, version
             FROM {_headsTable}
             WHERE 1 = 1
             """);

        if (filter.TenantId is not null)
        {
            sql.Append(" AND tenant_id = @tenantId");
        }

        if (filter.Attributes is not null)
        {
            for (var attributeIndex = 0; attributeIndex < filter.Attributes.Count; attributeIndex++)
            {
                sql.Append(
                    CultureInfo.InvariantCulture,
                    $" AND attributes ->> @attributeKey{attributeIndex} = @attributeValue{attributeIndex}");
            }
        }

        if (filter.OpenedBefore.HasValue)
        {
            sql.Append(" AND opened_at < @openedBefore");
        }

        if (!filter.IncludeExpired)
        {
            sql.Append(" AND expired = false");
        }

        sql.Append(" ORDER BY segment ASC;");

        using var command = scope.CreateCommand(sql.ToString());

        if (filter.TenantId is not null)
        {
            command.Parameters.AddWithValue("tenantId", filter.TenantId);
        }

        if (filter.Attributes is not null)
        {
            var attributeIndex = 0;

            foreach (var pair in filter.Attributes)
            {
                command.Parameters.AddWithValue($"attributeKey{attributeIndex}", pair.Key);
                command.Parameters.AddWithValue($"attributeValue{attributeIndex}", pair.Value);
                attributeIndex++;
            }
        }

        if (filter.OpenedBefore.HasValue)
        {
            command.Parameters.AddWithValue("openedBefore", filter.OpenedBefore.Value);
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return MapHead(reader);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Stored by checkpoint identity, so storing the same checkpoint twice overwrites rather than
    ///     duplicates: the identity stays stable across publish retries and anchors never diverge. The head
    ///     is untouched: the checkpoint lifecycle advances it past the cut with its own conditional append.
    /// </remarks>
    public async ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             INSERT INTO {_checkpointsTable}
                 (id, segment, sequence, document_count, hash, hash_scheme, kind, created_at,
                  signature_scheme, signature_value, label, publish_state, publish_attempts,
                  last_publish_error, signatures)
             VALUES (@id, @segment, @sequence, @documentCount, @hash, @hashScheme, @kind, @createdAt,
                 @signatureScheme, @signatureValue, @label, @publishState, @publishAttempts,
                 @lastPublishError, @signatures)
             ON CONFLICT (id) DO UPDATE SET
                 segment = EXCLUDED.segment,
                 sequence = EXCLUDED.sequence,
                 document_count = EXCLUDED.document_count,
                 hash = EXCLUDED.hash,
                 hash_scheme = EXCLUDED.hash_scheme,
                 kind = EXCLUDED.kind,
                 created_at = EXCLUDED.created_at,
                 signature_scheme = EXCLUDED.signature_scheme,
                 signature_value = EXCLUDED.signature_value,
                 label = EXCLUDED.label,
                 publish_state = EXCLUDED.publish_state,
                 publish_attempts = EXCLUDED.publish_attempts,
                 last_publish_error = EXCLUDED.last_publish_error,
                 signatures = EXCLUDED.signatures;
             """);
        command.Parameters.AddWithValue("id", checkpoint.Id);
        command.Parameters.AddWithValue("segment", checkpoint.Segment);
        command.Parameters.AddWithValue("sequence", checkpoint.Sequence);
        command.Parameters.AddWithValue("documentCount", checkpoint.Count);
        command.Parameters.AddWithValue("hash", checkpoint.Hash);
        command.Parameters.AddWithValue("hashScheme", checkpoint.HashScheme);
        command.Parameters.AddWithValue("kind", (int) checkpoint.Kind);
        command.Parameters.AddWithValue("createdAt", checkpoint.CreatedAt);
        AddNullableText(command, "signatureScheme", checkpoint.Signature?.Scheme);
        AddNullableText(command, "signatureValue", checkpoint.Signature?.Value);
        AddNullableText(command, "label", checkpoint.Label);
        command.Parameters.AddWithValue("publishState", (int) checkpoint.PublishState);
        command.Parameters.AddWithValue("publishAttempts", checkpoint.PublishAttempts);
        AddNullableText(command, "lastPublishError", checkpoint.LastPublishError);
        command.Parameters.AddWithValue(
            "signatures",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(checkpoint.Signatures, JsonOptions));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             SELECT id, segment, sequence, document_count, hash, hash_scheme, kind, created_at,
                 signature_scheme, signature_value, label, publish_state, publish_attempts,
                 last_publish_error, signatures::text
             FROM {_checkpointsTable}
             WHERE id = @id;
             """);
        command.Parameters.AddWithValue("id", checkpointId);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return MapCheckpoint(reader);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Checkpoints stream oldest first, so the publish retrier retries the longest-waiting checkpoint next.
    /// </remarks>
    public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             SELECT id, segment, sequence, document_count, hash, hash_scheme, kind, created_at,
                 signature_scheme, signature_value, label, publish_state, publish_attempts,
                 last_publish_error, signatures::text
             FROM {_checkpointsTable}
             WHERE publish_state = @pending
             ORDER BY created_at ASC, id ASC;
             """);
        command.Parameters.AddWithValue("pending", (int) CheckpointPublishState.Pending);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return MapCheckpoint(reader);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Marking a checkpoint that no longer exists is a no-op: erasure deletes stored checkpoints, and a
    ///     publish retry that loses that race has nothing left to mark.
    /// </remarks>
    public async ValueTask MarkPublishedAsync(
        Guid checkpointId,
        string? publishError,
        CancellationToken cancellationToken)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);

        if (publishError is null)
        {
            using var published = scope.CreateCommand(
                $"""
                 UPDATE {_checkpointsTable}
                 SET publish_state = @published,
                     publish_attempts = publish_attempts + 1,
                     last_publish_error = NULL
                 WHERE id = @id;
                 """);
            published.Parameters.AddWithValue("published", (int) CheckpointPublishState.Published);
            published.Parameters.AddWithValue("id", checkpointId);

            await published.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return;
        }

        using var failed = scope.CreateCommand(
            $"""
             UPDATE {_checkpointsTable}
             SET publish_state = @pending,
                 publish_attempts = publish_attempts + 1,
                 last_publish_error = @publishError
             WHERE id = @id;
             """);
        failed.Parameters.AddWithValue("pending", (int) CheckpointPublishState.Pending);
        failed.Parameters.AddWithValue("publishError", publishError);
        failed.Parameters.AddWithValue("id", checkpointId);

        await failed.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Erasure ignores whether a segment expired: an expired segment selected by the tenant, attribute, or
    ///         age predicates is erased with the rest, because erasure leaves nothing identifying. An empty filter
    ///         matches every segment.
    ///     </para>
    ///     <para>
    ///         Segments are matched by their heads. A staged document whose segment has no head yet is matched by
    ///         its own tenant when the filter names one, so erasure by tenant removes staged documents even before
    ///         the linker positions them.
    ///     </para>
    /// </remarks>
    public async ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var scope = await OpenScopeAsync(true, null, cancellationToken).ConfigureAwait(false);

        var matched = await ListMatchingSegmentsAsync(scope, filter, cancellationToken).ConfigureAwait(false);

        if (matched.Count > 0)
        {
            string[] segments = [.. matched];

            using (var deleteRecords = scope.CreateCommand(
                $"""
                 DELETE FROM {_recordsTable}
                 WHERE segment = ANY(@segments);
                 """))
            {
                deleteRecords.Parameters.AddWithValue("segments", segments);

                await deleteRecords.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using (var deleteHeads = scope.CreateCommand(
                $"""
                 DELETE FROM {_headsTable}
                 WHERE segment = ANY(@segments);
                 """))
            {
                deleteHeads.Parameters.AddWithValue("segments", segments);

                await deleteHeads.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using (var deleteCheckpoints = scope.CreateCommand(
                $"""
                 DELETE FROM {_checkpointsTable}
                 WHERE segment = ANY(@segments);
                 """))
            {
                deleteCheckpoints.Parameters.AddWithValue("segments", segments);

                await deleteCheckpoints.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (filter.TenantId is not null)
        {
            using var deleteStaged = scope.CreateCommand(
                $"""
                 DELETE FROM {_recordsTable}
                 WHERE sequence = 0
                   AND tenant_id = @tenantId;
                 """);
            deleteStaged.Parameters.AddWithValue("tenantId", filter.TenantId);

            await deleteStaged.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Retention, not erasure: the staged and linked rows go, while the head stays marked expired and
    ///     the stored checkpoints stay for verification. The head row is locked until the scope commits, so
    ///     a concurrent append waits for the expiry and is then rejected against the expired head.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The segment has no head, holds no records, or has already expired.</exception>
    public async ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        using var scope = await OpenScopeAsync(true, null, cancellationToken).ConfigureAwait(false);

        var stored = await ReadHeadForUpdateAsync(scope, segment, cancellationToken).ConfigureAwait(false);

        if (stored is null || stored.LastSequence == 0)
        {
            throw new InvalidOperationException(
                $"The audit segment '{segment}' holds no records, so it cannot expire.");
        }

        if (stored.Expired)
        {
            throw new InvalidOperationException(
                $"The audit segment '{segment}' has expired already, so it cannot expire again.");
        }

        using (var deleteRecords = scope.CreateCommand(
            $"""
             DELETE FROM {_recordsTable}
             WHERE segment = @segment;
             """))
        {
            deleteRecords.Parameters.AddWithValue("segment", segment);

            await deleteRecords.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using (var expireHead = scope.CreateCommand(
            $"""
             UPDATE {_headsTable}
             SET expired = true,
                 version = version + 1
             WHERE segment = @segment;
             """))
        {
            expireHead.Parameters.AddWithValue("segment", segment);

            await expireHead.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        using var scope = await OpenScopeAsync(false, null, cancellationToken).ConfigureAwait(false);
        using var command = scope.CreateCommand(
            $"""
             SELECT id, segment, sequence, document_count, hash, hash_scheme, kind, created_at,
                 signature_scheme, signature_value, label, publish_state, publish_attempts,
                 last_publish_error, signatures::text
             FROM {_checkpointsTable}
             WHERE segment = @segment
             ORDER BY sequence DESC, created_at DESC, id DESC
             LIMIT 1;
             """);
        command.Parameters.AddWithValue("segment", segment);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return MapCheckpoint(reader);
    }

    /// <summary>
    ///     Opens the connection scope one store operation runs on.
    /// </summary>
    /// <param name="useTransaction">Whether the scope owns a transaction it commits.</param>
    /// <param name="ambient">
    ///     The provider of the business transaction to join when it reports one, or <see langword="null" /> to run
    ///     outside any business transaction.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the open.</param>
    /// <returns>The scope the operation creates its commands from.</returns>
    /// <remarks>
    ///     A store bound through <see cref="UseExistingConnection" /> always runs on its bound connection and
    ///     transaction. Otherwise the transaction <paramref name="ambient" /> reports is joined when it has one;
    ///     every other operation opens its own pooled connection, with its own transaction when
    ///     <paramref name="useTransaction" /> is <see langword="true" />. The scope commits and disposes only what
    ///     it owns: a bound or ambient transaction is left to its owner.
    /// </remarks>
    private async ValueTask<AuditStoreScope> OpenScopeAsync(
        bool useTransaction,
        IPostgreSqlTransactionProvider? ambient,
        CancellationToken cancellationToken)
    {
        if (_transactionConnection is not null && _transaction is not null)
        {
            return AuditStoreScope.FromAmbient(_transactionConnection, _transaction);
        }

        if (ambient is not null && ambient.TryGetCurrent(out var connection, out var transaction))
        {
            return AuditStoreScope.FromAmbient(connection, transaction);
        }

        return await AuditStoreScope.OpenOwnedAsync(_dataSource, useTransaction, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Stages an unlinked document, joining the business transaction a provider reports when it has one.
    /// </summary>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="ambient">
    ///     The provider of the business transaction to join, or <see langword="null" /> to stage on the store's own
    ///     connection.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    private async ValueTask StageCoreAsync(
        string segment,
        TDocument document,
        IPostgreSqlTransactionProvider? ambient,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        using var scope = await OpenScopeAsync(false, ambient, cancellationToken).ConfigureAwait(false);
        await StageInScopeAsync(scope, segment, document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Maps a record in the scope its staged row is written in and stages the mapped document there, so anything
    ///     the mapper writes through the connection commits or rolls back with the record.
    /// </summary>
    /// <param name="ambient">
    ///     The provider of the business transaction to join, which is the success path, or <see langword="null" />
    ///     for the out-of-band path, which opens its own connection and transaction and commits them before
    ///     returning.
    /// </param>
    /// <param name="map">Maps the record with the scope's connection and transaction.</param>
    /// <param name="segmentOf">Returns the segment of the mapped document.</param>
    /// <param name="cancellationToken">The token used to cancel the mapping and the staging.</param>
    /// <returns>The staged document and its segment.</returns>
    internal async ValueTask<(TDocument Document, string Segment)> MapAndStageAsync(
        IPostgreSqlTransactionProvider? ambient,
        Func<NpgsqlConnection, NpgsqlTransaction?, ValueTask<TDocument>> map,
        Func<TDocument, string> segmentOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(segmentOf);

        using var scope = await OpenScopeAsync(true, ambient, cancellationToken).ConfigureAwait(false);
        var document = await map(scope.Connection, scope.Transaction).ConfigureAwait(false);
        var segment = segmentOf(document);

        await StageInScopeAsync(scope, segment, document, cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return (document, segment);
    }

    /// <summary>
    ///     Stages an unlinked document on an open scope.
    /// </summary>
    /// <param name="scope">The scope to write on.</param>
    /// <param name="segment">The segment the document belongs to. The store assigns the document to it.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    /// <exception cref="InvalidOperationException">The document already carries a chain position.</exception>
    private async ValueTask StageInScopeAsync(
        AuditStoreScope scope,
        string segment,
        TDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(document);

        var key = DocumentKey(document);

        if (document.Sequence != 0 ||
            document.PreviousHash is not null ||
            document.Hash is not null ||
            document.HashScheme is not null)
        {
            throw new InvalidOperationException(
                $"The audit document '{key}' already carries a chain position and cannot be staged as unlinked.");
        }

        document.Segment = segment;

        using var command = scope.CreateCommand(
            $"""
             INSERT INTO {_recordsTable} AS existing
                 (document_key, segment, sequence, previous_hash, hash, hash_scheme, tenant_id, document, staged_at)
             VALUES (@key, @segment, 0, NULL, NULL, NULL, @tenantId, @document, @stagedAt)
             ON CONFLICT (document_key) DO UPDATE SET
                 segment = EXCLUDED.segment,
                 tenant_id = EXCLUDED.tenant_id,
                 document = EXCLUDED.document,
                 staged_at = EXCLUDED.staged_at
             WHERE existing.sequence = 0;
             """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("segment", segment);
        AddNullableText(command, "tenantId", DocumentTenant(document));
        command.Parameters.AddWithValue(
            "document",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(document, JsonOptions));
        command.Parameters.AddWithValue("stagedAt", _clock.GetUtcNow());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads the head of a segment and locks its row until the scope commits.
    /// </summary>
    /// <param name="scope">The scope the read runs on.</param>
    /// <param name="segment">The segment.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The head, or <see langword="null" /> when the segment has no head yet.</returns>
    /// <remarks>
    ///     The row lock serializes concurrent appends: the second writer waits for the winner to commit, then
    ///     reads the moved head and reports a conflict against it. Two first appends race on the head insert
    ///     instead, and the primary key reports the loser.
    /// </remarks>
    private async ValueTask<AuditSegmentHead?> ReadHeadForUpdateAsync(
        AuditStoreScope scope,
        string segment,
        CancellationToken cancellationToken)
    {
        using var command = scope.CreateCommand(
            $"""
             SELECT segment, tenant_id, attributes::text, opened_at, last_sequence, last_hash,
                 document_count, last_checkpoint_sequence, last_checkpoint_at, expired, version
             FROM {_headsTable}
             WHERE segment = @segment
             FOR UPDATE;
             """);
        command.Parameters.AddWithValue("segment", segment);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return MapHead(reader);
    }

    /// <summary>
    ///     Inserts the head a first append creates from the described identity.
    /// </summary>
    /// <param name="scope">The scope the insert runs on.</param>
    /// <param name="updated">The head after the append.</param>
    /// <param name="cancellationToken">The token used to cancel the insert.</param>
    /// <returns>A task representing the asynchronous insert.</returns>
    private async ValueTask InsertHeadAsync(
        AuditStoreScope scope,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        using var command = scope.CreateCommand(
            $"""
             INSERT INTO {_headsTable}
                 (segment, tenant_id, attributes, opened_at, last_sequence, last_hash, document_count,
                  last_checkpoint_sequence, last_checkpoint_at, expired, version)
             VALUES (@segment, @tenantId, @attributes, @openedAt, @lastSequence, @lastHash, @documentCount,
                 @lastCheckpointSequence, @lastCheckpointAt, @expired, @version);
             """);
        command.Parameters.AddWithValue("segment", updated.Info.Key);
        AddNullableText(command, "tenantId", updated.Info.TenantId);
        command.Parameters.AddWithValue(
            "attributes",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(updated.Info.Attributes, JsonOptions));
        command.Parameters.AddWithValue("openedAt", updated.Info.OpenedAt);
        command.Parameters.AddWithValue("lastSequence", updated.LastSequence);
        AddNullableText(command, "lastHash", updated.LastHash);
        command.Parameters.AddWithValue("documentCount", updated.Count);
        AddParameter(command, "lastCheckpointSequence", NpgsqlDbType.Bigint, updated.LastCheckpointSequence);
        AddParameter(command, "lastCheckpointAt", NpgsqlDbType.TimestampTz, updated.LastCheckpointAt);
        command.Parameters.AddWithValue("expired", updated.Expired);
        command.Parameters.AddWithValue("version", updated.Version);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Moves a stored head to its position after an append, keeping the stored identity.
    /// </summary>
    /// <param name="scope">The scope the update runs on.</param>
    /// <param name="updated">The head after the append.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    /// <remarks>
    ///     The tenant, the attributes, and when the segment opened stay as stored: the engine carries them
    ///     unchanged on every later append, so writing them again could only diverge them. The first append
    ///     stores them through <see cref="InsertHeadAsync" /> instead.
    /// </remarks>
    private async ValueTask UpdateHeadAsync(
        AuditStoreScope scope,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        using var command = scope.CreateCommand(
            $"""
             UPDATE {_headsTable}
             SET last_sequence = @lastSequence,
                 last_hash = @lastHash,
                 document_count = @documentCount,
                 last_checkpoint_sequence = @lastCheckpointSequence,
                 last_checkpoint_at = @lastCheckpointAt,
                 expired = @expired,
                 version = @version
             WHERE segment = @segment;
             """);
        command.Parameters.AddWithValue("segment", updated.Info.Key);
        command.Parameters.AddWithValue("lastSequence", updated.LastSequence);
        AddNullableText(command, "lastHash", updated.LastHash);
        command.Parameters.AddWithValue("documentCount", updated.Count);
        AddParameter(command, "lastCheckpointSequence", NpgsqlDbType.Bigint, updated.LastCheckpointSequence);
        AddParameter(command, "lastCheckpointAt", NpgsqlDbType.TimestampTz, updated.LastCheckpointAt);
        command.Parameters.AddWithValue("expired", updated.Expired);
        command.Parameters.AddWithValue("version", updated.Version);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes linked documents: staged rows are linked in place by identity, new rows are inserted.
    /// </summary>
    /// <param name="scope">The scope the writes run on.</param>
    /// <param name="segment">The segment the append targets.</param>
    /// <param name="documents">The linked documents to write, in ascending sequence order.</param>
    /// <param name="cancellationToken">The token used to cancel the writes.</param>
    /// <returns>A task representing the asynchronous writes.</returns>
    /// <exception cref="InvalidOperationException">
    ///     A document is already linked, or its staged row belongs to another segment.
    /// </exception>
    /// <remarks>
    ///     The staged timestamp stays as originally staged when a staged row is linked in place: it records when
    ///     the record arrived, not when it linked. A document with no staged row, such as a genesis document,
    ///     is stamped now.
    /// </remarks>
    private async ValueTask PersistLinkedDocumentsAsync(
        AuditStoreScope scope,
        string segment,
        IReadOnlyList<TDocument> documents,
        CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            var key = DocumentKey(document);

            using var command = scope.CreateCommand(
                $"""
                 INSERT INTO {_recordsTable} AS existing
                     (document_key, segment, sequence, previous_hash, hash, hash_scheme, tenant_id, document, staged_at)
                 VALUES (@key, @segment, @sequence, @previousHash, @hash, @hashScheme, @tenantId, @document, @stagedAt)
                 ON CONFLICT (document_key) DO UPDATE SET
                     segment = EXCLUDED.segment,
                     sequence = EXCLUDED.sequence,
                     previous_hash = EXCLUDED.previous_hash,
                     hash = EXCLUDED.hash,
                     hash_scheme = EXCLUDED.hash_scheme,
                     tenant_id = EXCLUDED.tenant_id,
                     document = EXCLUDED.document
                 WHERE existing.sequence = 0 AND existing.segment = @segment;
                 """);
            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("segment", segment);
            command.Parameters.AddWithValue("sequence", document.Sequence);
            AddNullableText(command, "previousHash", document.PreviousHash);
            AddNullableText(command, "hash", document.Hash);
            AddNullableText(command, "hashScheme", document.HashScheme);
            AddNullableText(command, "tenantId", DocumentTenant(document));
            command.Parameters.AddWithValue(
                "document",
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(document, JsonOptions));
            command.Parameters.AddWithValue("stagedAt", _clock.GetUtcNow());

            var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (written == 0)
            {
                throw new InvalidOperationException(
                    $"The audit document '{key}' is already linked or was staged in another segment, so it cannot " +
                    $"be appended to '{segment}'. Repositioning a committed record would rewrite the chain suffix.");
            }
        }
    }

    /// <summary>
    ///     Fails when an append carries a document of another segment or a sequence that does not increase.
    /// </summary>
    /// <param name="segment">The segment the append targets.</param>
    /// <param name="documents">The documents the append carries.</param>
    /// <exception cref="InvalidOperationException">A document belongs to another segment or breaks the order.</exception>
    private static void ThrowIfUnexpectedDocuments(string segment, IReadOnlyList<TDocument> documents)
    {
        var previous = 0L;

        foreach (var document in documents)
        {
            if (!string.Equals(document.Segment, segment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The audit document carries segment '{document.Segment}' but the append targets '{segment}'.");
            }

            if (document.Sequence <= previous)
            {
                throw new InvalidOperationException(
                    $"The audit documents of segment '{segment}' must carry strictly increasing sequences, but sequence {document.Sequence} follows {previous}.");
            }

            previous = document.Sequence;
        }
    }

    /// <summary>
    ///     Lists the segments a filter matches, ignoring whether they expired.
    /// </summary>
    /// <param name="scope">The scope the listing runs on.</param>
    /// <param name="filter">The segments to match.</param>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The matching segment keys.</returns>
    /// <remarks>
    ///     Erasure leaves nothing identifying, so it matches expired segments with the rest. Attribute
    ///     predicates carry over from <see cref="ListHeadsAsync" />: a segment matches only when it carries
    ///     every named value.
    /// </remarks>
    private async ValueTask<IReadOnlyList<string>> ListMatchingSegmentsAsync(
        AuditStoreScope scope,
        AuditSegmentFilter filter,
        CancellationToken cancellationToken)
    {
        var sql = new StringBuilder();
        sql.Append(
            CultureInfo.InvariantCulture,
            $"""
             SELECT segment
             FROM {_headsTable}
             WHERE 1 = 1
             """);

        if (filter.TenantId is not null)
        {
            sql.Append(" AND tenant_id = @tenantId");
        }

        if (filter.Attributes is not null)
        {
            for (var attributeIndex = 0; attributeIndex < filter.Attributes.Count; attributeIndex++)
            {
                sql.Append(
                    CultureInfo.InvariantCulture,
                    $" AND attributes ->> @attributeKey{attributeIndex} = @attributeValue{attributeIndex}");
            }
        }

        if (filter.OpenedBefore.HasValue)
        {
            sql.Append(" AND opened_at < @openedBefore");
        }

        sql.Append(';');

        using var command = scope.CreateCommand(sql.ToString());

        if (filter.TenantId is not null)
        {
            command.Parameters.AddWithValue("tenantId", filter.TenantId);
        }

        if (filter.Attributes is not null)
        {
            var attributeIndex = 0;

            foreach (var pair in filter.Attributes)
            {
                command.Parameters.AddWithValue($"attributeKey{attributeIndex}", pair.Key);
                command.Parameters.AddWithValue($"attributeValue{attributeIndex}", pair.Value);
                attributeIndex++;
            }
        }

        if (filter.OpenedBefore.HasValue)
        {
            command.Parameters.AddWithValue("openedBefore", filter.OpenedBefore.Value);
        }

        List<string> matched = [];

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matched.Add(reader.GetString(0));
        }

        return matched;
    }

    /// <summary>
    ///     Returns the text identity of a stored document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The identity converted with the invariant culture.</returns>
    /// <exception cref="InvalidOperationException">The document carries no identity.</exception>
    private string DocumentKey(TDocument document)
    {
        var value = _identityProperty.GetValue(document);

        if (value is null)
        {
            throw new InvalidOperationException(
                $"The audit document of type '{typeof(TDocument).FullName}' carries no identity. Assign its 'Id' before staging it.");
        }

        var key = Convert.ToString(value, CultureInfo.InvariantCulture);

        return key ?? string.Empty;
    }

    /// <summary>
    ///     Returns the tenant a stored document belongs to, when the document type carries one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The tenant, or <see langword="null" /> when the type carries no tenant.</returns>
    private string? DocumentTenant(TDocument document)
    {
        if (_tenantProperty is null)
        {
            return null;
        }

        var tenant = _tenantProperty.GetValue(document) as string;

        return string.IsNullOrWhiteSpace(tenant) ? null : tenant;
    }

    /// <summary>
    ///     Resolves the readable identity property of the stored document type.
    /// </summary>
    /// <returns>The <c>Id</c> property, matched case-insensitively.</returns>
    /// <exception cref="InvalidOperationException">The stored document type carries no readable <c>Id</c>.</exception>
    private static PropertyInfo ResolveIdentityProperty()
    {
        var property = typeof(TDocument).GetProperty(
            "Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.CanRead)
        {
            throw new InvalidOperationException(
                $"The audit document type '{typeof(TDocument).FullName}' carries no readable 'Id' property. " +
                "PostgreSQL audit documents need an identity, so staged documents can be ordered, matched, and erased.");
        }

        return property;
    }

    /// <summary>
    ///     Resolves the readable tenant property of the stored document type, when it has one.
    /// </summary>
    /// <returns>The string <c>TenantId</c> property, or <see langword="null" /> when the type carries none.</returns>
    private static PropertyInfo? ResolveTenantProperty()
    {
        var property = typeof(TDocument).GetProperty(
            "TenantId",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.CanRead || property.PropertyType != typeof(string))
        {
            return null;
        }

        return property;
    }

    /// <summary>
    ///     Deserializes one stored document.
    /// </summary>
    /// <param name="json">The stored JSON document.</param>
    /// <param name="key">The document identity, named when deserialization fails.</param>
    /// <param name="segment">The segment the document was read from, named when deserialization fails.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidOperationException">The stored JSON does not deserialize to a document.</exception>
    private static TDocument DeserializeDocument(string json, string key, string segment)
    {
        return JsonSerializer.Deserialize<TDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                $"The stored audit document '{key}' of segment '{segment}' does not deserialize to '{typeof(TDocument).FullName}'.");
    }

    /// <summary>
    ///     Reads one head row.
    /// </summary>
    /// <param name="reader">The reader positioned on the head row to read.</param>
    /// <returns>The head.</returns>
    private static AuditSegmentHead MapHead(NpgsqlDataReader reader)
    {
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(2),
                JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var info = new AuditSegmentInfo(
            reader.GetString(0),
            GetNullableString(reader, 1),
            attributes,
            reader.GetFieldValue<DateTimeOffset>(3));

        return new AuditSegmentHead(
            info,
            reader.GetInt64(4),
            GetNullableString(reader, 5),
            reader.GetInt64(6),
            GetNullableInt64(reader, 7),
            GetNullableDateTimeOffset(reader, 8),
            reader.GetBoolean(9),
            reader.GetInt64(10));
    }

    /// <summary>
    ///     Reads one checkpoint row.
    /// </summary>
    /// <param name="reader">The reader positioned on the checkpoint row to read.</param>
    /// <returns>The checkpoint.</returns>
    private static AuditCheckpoint MapCheckpoint(NpgsqlDataReader reader)
    {
        var scheme = GetNullableString(reader, 8);
        var value = GetNullableString(reader, 9);
        var signatures = JsonSerializer.Deserialize<List<AuditCheckpointSignature>>(
                reader.GetString(14),
                JsonOptions)
            ?? [];

        return new AuditCheckpoint(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            (AuditCheckpointKind) reader.GetInt32(6),
            reader.GetFieldValue<DateTimeOffset>(7))
        {
            Signature = scheme is null || value is null ? null : new AuditCheckpointSignature(scheme, value),
            Label = GetNullableString(reader, 10),
            PublishState = (CheckpointPublishState) reader.GetInt32(11),
            PublishAttempts = reader.GetInt32(12),
            LastPublishError = GetNullableString(reader, 13),
            Signatures = signatures
        };
    }

    /// <summary>
    ///     Reads an optional text column.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to read.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The string when the column is not database null; otherwise, null.</returns>
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    ///     Reads an optional long column.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to read.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value when the column is not database null; otherwise, null.</returns>
    private static long? GetNullableInt64(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    /// <summary>
    ///     Reads an optional timestamp column.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to read.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value when the column is not database null; otherwise, null.</returns>
    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    /// <summary>
    ///     Adds a parameter with an explicit PostgreSQL type, writing database null for a missing value.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="dbType">The explicit PostgreSQL type, so null comparisons compile in SQL.</param>
    /// <param name="value">The value, or <see langword="null" /> for database null.</param>
    private static void AddParameter(NpgsqlCommand command, string name, NpgsqlDbType dbType, object? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, dbType)
        {
            Value = value ?? DBNull.Value
        });
    }

    /// <summary>
    ///     Adds a nullable text parameter with an explicit PostgreSQL type so null checks compile in SQL.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The optional text value.</param>
    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        AddParameter(command, name, NpgsqlDbType.Text, value);
    }

    /// <summary>
    ///     Decides whether a PostgreSQL state reports a lost head write race.
    /// </summary>
    /// <param name="sqlState">The five-character state the storage error reported.</param>
    /// <returns>
    ///     <see langword="true" /> for a unique violation (<c>23505</c>), a serialization failure (<c>40001</c>), or a
    ///     deadlock (<c>40P01</c>); otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsHeadWriteState(string sqlState)
    {
        return sqlState is "23505" or "40001" or "40P01";
    }

    /// <summary>
    ///     The connection one store operation runs on, with the transaction it commits when it owns one.
    /// </summary>
    /// <remarks>
    ///     A scope either borrows a bound or ambient connection and transaction, which it never commits or
    ///     disposes, or opens its own pooled connection, with its own transaction for multi-statement writes.
    ///     Disposing a scope that owns an uncommitted transaction rolls it back.
    /// </remarks>
    private sealed class AuditStoreScope : IAsyncDisposable, IDisposable
    {
        /// <summary>
        ///     The connection the scope opened itself, or <see langword="null" /> when it borrows one.
        /// </summary>
        private readonly NpgsqlConnection? _ownedConnection;

        /// <summary>
        ///     The transaction the scope opened itself, or <see langword="null" /> when it borrows one or runs
        ///     without one.
        /// </summary>
        private readonly NpgsqlTransaction? _ownedTransaction;

        /// <summary>
        ///     The connection commands run on: the owned one, or the borrowed bound or ambient one.
        /// </summary>
        private readonly NpgsqlConnection _connection;

        /// <summary>
        ///     The transaction commands run in, or <see langword="null" /> when statements autocommit.
        /// </summary>
        private readonly NpgsqlTransaction? _transaction;

        /// <summary>
        ///     Whether the owned transaction committed. Guarded by the scope, which is never shared.
        /// </summary>
        private bool _committed;

        /// <summary>
        ///     Whether the scope disposed. Guarded by the scope, which is never shared.
        /// </summary>
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AuditStoreScope" /> class.
        /// </summary>
        /// <param name="connection">The connection commands run on.</param>
        /// <param name="transaction">The transaction commands run in, or <see langword="null" /> when statements autocommit.</param>
        /// <param name="ownedConnection">The connection the scope opened itself, or <see langword="null" /> when it borrows one.</param>
        /// <param name="ownedTransaction">The transaction the scope opened itself, or <see langword="null" /> when it borrows one or runs without one.</param>
        private AuditStoreScope(
            NpgsqlConnection connection,
            NpgsqlTransaction? transaction,
            NpgsqlConnection? ownedConnection,
            NpgsqlTransaction? ownedTransaction)
        {
            _connection = connection;
            _transaction = transaction;
            _ownedConnection = ownedConnection;
            _ownedTransaction = ownedTransaction;
        }

        /// <summary>
        ///     Opens a scope on a borrowed connection and transaction, which the scope never commits or disposes.
        /// </summary>
        /// <param name="connection">The borrowed open connection.</param>
        /// <param name="transaction">The borrowed transaction commands run in.</param>
        /// <returns>The scope.</returns>
        public static AuditStoreScope FromAmbient(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);

            return new AuditStoreScope(connection, transaction, null, null);
        }

        /// <summary>
        ///     Opens a scope on its own pooled connection, with its own transaction when asked for one.
        /// </summary>
        /// <param name="dataSource">The data source the connection opens from.</param>
        /// <param name="useTransaction">Whether the scope opens a transaction it commits.</param>
        /// <param name="cancellationToken">The token used to cancel the open.</param>
        /// <returns>The scope.</returns>
        public static async ValueTask<AuditStoreScope> OpenOwnedAsync(
            NpgsqlDataSource dataSource,
            bool useTransaction,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dataSource);

            var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (!useTransaction)
            {
                return new AuditStoreScope(connection, null, connection, null);
            }

            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            return new AuditStoreScope(connection, transaction, connection, transaction);
        }

        /// <summary>
        ///     Gets the connection the scope runs on.
        /// </summary>
        public NpgsqlConnection Connection => _connection;

        /// <summary>
        ///     Gets the transaction the scope runs in, or <see langword="null" /> when it runs without one.
        /// </summary>
        public NpgsqlTransaction? Transaction => _transaction;

        /// <summary>
        ///     Creates a command on the scope connection and transaction.
        /// </summary>
        /// <param name="sql">The SQL batch text.</param>
        /// <returns>The command.</returns>
        public NpgsqlCommand CreateCommand(string sql)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _transaction;

            return command;
        }

        /// <summary>
        ///     Commits the owned transaction, or does nothing when the scope borrows one or runs without one.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the commit.</param>
        /// <returns>A task that completes when the transaction commits.</returns>
        public async ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            if (_ownedTransaction is not null && !_committed)
            {
                await _ownedTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _committed = true;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_ownedTransaction is not null)
            {
                if (!_committed)
                {
                    await _ownedTransaction.RollbackAsync().ConfigureAwait(false);
                }

                await _ownedTransaction.DisposeAsync().ConfigureAwait(false);
            }

            if (_ownedConnection is not null)
            {
                await _ownedConnection.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_ownedTransaction is not null)
            {
                if (!_committed)
                {
                    _ownedTransaction.Rollback();
                }

                _ownedTransaction.Dispose();
            }

            _ownedConnection?.Dispose();
        }
    }
}
