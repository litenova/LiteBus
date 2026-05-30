using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core inbox store that implements writer, lease, and state roles.
/// </summary>
/// <remarks>
///     <para>
///         PostgreSQL uses <c>FOR UPDATE SKIP LOCKED</c> for leasing. The in-memory provider uses a
///         process-wide lock with the same visibility rules so unit tests can run without a database.
///     </para>
///     <para>
///         Applications own the <see cref="DbContext" /> and migrations. Call
///         <see cref="InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> from
///         <c>OnModelCreating</c> to align schema with this store.
///     </para>
/// </remarks>
public sealed class EfCoreInboxStore : IInboxStore, IInboxLeaseStore, IInboxStateStore
{
    /// <summary>
    ///     Provider name for the Entity Framework Core in-memory database.
    /// </summary>
    private const string InMemoryProviderName = "Microsoft.EntityFrameworkCore.InMemory";

    /// <summary>
    ///     Provider name for the Npgsql Entity Framework Core provider.
    /// </summary>
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    ///     PostgreSQL lease SQL with a <c>{TABLE}</c> placeholder for the qualified table name.
    /// </summary>
    private const string PostgreSqlLeaseSql = """
                                            WITH candidates AS (
                                                SELECT command_id
                                                FROM {TABLE}
                                                WHERE
                                                    ((status IN ({0}, {1}) AND (visible_after IS NULL OR visible_after <= {2}))
                                                     OR (status = {3} AND lease_expires_at IS NOT NULL AND lease_expires_at <= {2}))
                                                ORDER BY created_at ASC
                                                LIMIT {4}
                                                FOR UPDATE SKIP LOCKED
                                            )
                                            UPDATE {TABLE} AS inbox
                                            SET
                                                status = {3},
                                                lease_owner = {5},
                                                lease_expires_at = {6},
                                                attempt_count = inbox.attempt_count + 1
                                            FROM candidates
                                            WHERE inbox.command_id = candidates.command_id
                                            RETURNING
                                                inbox.command_id,
                                                inbox.contract_name,
                                                inbox.contract_version,
                                                inbox.payload,
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

    /// <summary>
    ///     Synchronizes in-memory leasing when multiple workers run in one process.
    /// </summary>
    private readonly object _inMemoryLeaseLock = new();

    /// <summary>
    ///     Resolves a database context for direct factory construction used in tests.
    /// </summary>
    private readonly Func<CancellationToken, Task<IInboxDbContext>>? _contextFactory;

    /// <summary>
    ///     The Entity Framework Core context type registered for scoped resolution.
    /// </summary>
    private readonly Type? _dbContextType;

    /// <summary>
    ///     Store options that define schema and table names for raw SQL leasing.
    /// </summary>
    private readonly EfCoreInboxStoreOptions _options;

    /// <summary>
    ///     Creates scopes that resolve application database contexts from dependency injection.
    /// </summary>
    private readonly IServiceScopeFactory? _scopeFactory;

    /// <summary>
    ///     The quoted qualified table name used by PostgreSQL lease SQL.
    /// </summary>
    private readonly string _qualifiedTableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStore" /> class using dependency injection scopes.
    /// </summary>
    /// <param name="scopeFactory">The scope factory that resolves the application database context.</param>
    /// <param name="dbContextType">The database context type that implements <see cref="IInboxDbContext" />.</param>
    /// <param name="options">The store options.</param>
    public EfCoreInboxStore(
        IServiceScopeFactory scopeFactory,
        Type dbContextType,
        EfCoreInboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(dbContextType);
        ArgumentNullException.ThrowIfNull(options);

        if (!typeof(IInboxDbContext).IsAssignableFrom(dbContextType))
        {
            throw new ArgumentException(
                $"The database context type '{dbContextType.FullName}' must implement {nameof(IInboxDbContext)}.",
                nameof(dbContextType));
        }

        _scopeFactory = scopeFactory;
        _dbContextType = dbContextType;
        _options = options;
        _qualifiedTableName = QualifyTableName(options.SchemaName, options.TableName);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStore" /> class using a custom context factory.
    /// </summary>
    /// <param name="contextFactory">A factory that returns a context for one store operation.</param>
    /// <param name="options">The store options.</param>
    public EfCoreInboxStore(
        Func<CancellationToken, Task<IInboxDbContext>> contextFactory,
        EfCoreInboxStoreOptions options)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _qualifiedTableName = QualifyTableName(options.SchemaName, options.TableName);
    }

    /// <inheritdoc />
    public async Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return await ExecuteAsync(async (context, token) =>
        {
            var existing = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return ToEnvelope(existing);
            }

            var entity = ToEntity(envelope);
            context.InboxMessages.Add(entity);

            try
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
                var stored = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token)
                    .ConfigureAwait(false);
                return ToEnvelope(stored ?? entity);
            }
            catch (DbUpdateException)
            {
                var stored = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token)
                    .ConfigureAwait(false);

                if (stored is null)
                {
                    throw;
                }

                DetachFailedInsert(context, entity);
                return ToEnvelope(stored);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
        InboxLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteAsync(async (context, token) =>
        {
            if (IsInMemoryProvider(context))
            {
                return await LeasePendingInMemoryAsync(context, request, token).ConfigureAwait(false);
            }

            if (IsNpgsqlProvider(context))
            {
                return await LeasePendingPostgreSqlAsync(context, request, token).ConfigureAwait(false);
            }

            throw new NotSupportedException(
                $"Leasing is not supported for Entity Framework provider '{GetProviderName(context)}'. " +
                "Use the in-memory provider for unit tests or Npgsql.EntityFrameworkCore.PostgreSQL for PostgreSQL.");
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.InboxMessages
                .SingleOrDefaultAsync(message => message.Id == commandId, token)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            entity.Status = InboxStatus.Completed;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = null;
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.InboxMessages
                .SingleOrDefaultAsync(message => message.Id == failure.Id, token)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            entity.Status = InboxStatus.Failed;
            entity.VisibleAfter = failure.VisibleAfter;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = failure.Error;
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.InboxMessages
                .SingleOrDefaultAsync(message => message.Id == deadLetter.Id, token)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            entity.Status = InboxStatus.DeadLettered;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = deadLetter.Reason;
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    ///     Runs one store operation against a resolved database context.
    /// </summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="action">The action that uses the context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The action result.</returns>
    private async Task<TResult> ExecuteAsync<TResult>(
        Func<IInboxDbContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        if (_contextFactory is not null)
        {
            var context = await _contextFactory(cancellationToken).ConfigureAwait(false);
            return await action(context, cancellationToken).ConfigureAwait(false);
        }

        if (_scopeFactory is null || _dbContextType is null)
        {
            throw new InvalidOperationException("The inbox store is not configured with a context factory or scope factory.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var contextFromScope = (IInboxDbContext)scope.ServiceProvider.GetRequiredService(_dbContextType);
        return await action(contextFromScope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs one store operation that does not return a value.
    /// </summary>
    /// <param name="action">The action that uses the context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private Task ExecuteAsync(
        Func<IInboxDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async (context, token) =>
        {
            await action(context, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    ///     Leases commands using PostgreSQL row locking semantics.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased command envelopes.</returns>
    private async Task<IReadOnlyList<InboxEnvelope>> LeasePendingPostgreSqlAsync(
        IInboxDbContext context,
        InboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("PostgreSQL leasing requires a DbContext-backed inbox database context.");
        }

        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        var sql = PostgreSqlLeaseSql.Replace("{TABLE}", _qualifiedTableName, StringComparison.Ordinal);

        var rows = await dbContext.Database
            .SqlQueryRaw<InboxMessageLeaseRow>(
                sql,
                (int)InboxStatus.Pending,
                (int)InboxStatus.Failed,
                request.Now,
                (int)InboxStatus.Processing,
                request.BatchSize,
                request.LeaseOwner,
                leaseExpiresAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToEnvelope).ToArray();
    }

    /// <summary>
    ///     Leases commands using in-memory queries guarded by a process lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased command envelopes.</returns>
    private Task<IReadOnlyList<InboxEnvelope>> LeasePendingInMemoryAsync(
        IInboxDbContext context,
        InboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        lock (_inMemoryLeaseLock)
        {
            return Task.FromResult(LeasePendingInMemoryCore(context, request));
        }
    }

    /// <summary>
    ///     Leases commands from the in-memory provider inside the in-memory lease lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <returns>The leased command envelopes.</returns>
    private static IReadOnlyList<InboxEnvelope> LeasePendingInMemoryCore(
        IInboxDbContext context,
        InboxLeaseRequest request)
    {
        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        var candidates = context.InboxMessages
            .AsEnumerable()
            .Where(message => IsAvailable(message, request.Now))
            .OrderBy(message => message.CreatedAt)
            .Take(request.BatchSize)
            .ToList();

        foreach (var message in candidates)
        {
            message.Status = InboxStatus.Processing;
            message.LeaseOwner = request.LeaseOwner;
            message.LeaseExpiresAt = leaseExpiresAt;
            message.AttemptCount++;
        }

        if (context is DbContext dbContext)
        {
            dbContext.SaveChanges();
        }

        return candidates.Select(ToEnvelope).ToArray();
    }

    /// <summary>
    ///     Determines whether a command is eligible for leasing at the supplied time.
    /// </summary>
    /// <param name="message">The persisted message.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns><see langword="true" /> when the message can be leased; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(InboxMessageEntity message, DateTimeOffset now)
    {
        return (message.Status is InboxStatus.Pending or InboxStatus.Failed
                && (message.VisibleAfter is null || message.VisibleAfter <= now))
               || (message.Status == InboxStatus.Processing
                   && message.LeaseExpiresAt is not null
                   && message.LeaseExpiresAt <= now);
    }

    /// <summary>
    ///     Finds an existing row after a duplicate insert attempt.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="commandId">The command identifier from the attempted insert.</param>
    /// <param name="idempotencyKey">The idempotency key from the attempted insert.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The existing entity when found; otherwise, <see langword="null" />.</returns>
    private static async Task<InboxMessageEntity?> FindExistingEntityAsync(
        IInboxDbContext context,
        Guid commandId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await context.InboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == commandId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await context.InboxMessages
            .AsNoTracking()
            .Where(message => message.Id == commandId || message.IdempotencyKey == idempotencyKey)
            .OrderBy(message => message.Id == commandId ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists pending changes through the underlying <see cref="DbContext" />.
    /// </summary>
    /// <param name="context">The inbox database context.</param>
    /// <param name="cancellationToken">A token that cancels the save operation.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    private static Task SaveChangesAsync(IInboxDbContext context, CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("The inbox database context must inherit from DbContext.");
        }

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     Gets the Entity Framework provider name for error reporting.
    /// </summary>
    /// <param name="context">The inbox database context.</param>
    /// <returns>The provider name when available; otherwise, an unknown placeholder.</returns>
    private static string GetProviderName(IInboxDbContext context)
    {
        return context is DbContext dbContext
            ? dbContext.Database.ProviderName ?? "unknown"
            : "unknown";
    }

    /// <summary>
    ///     Detaches a failed insert entity so the context can continue tracking other rows.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="entity">The entity that failed to insert.</param>
    private static void DetachFailedInsert(IInboxDbContext context, InboxMessageEntity entity)
    {
        if (context is DbContext dbContext)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    /// <summary>
    ///     Returns whether the context uses the in-memory provider.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <returns><see langword="true" /> when the in-memory provider is active.</returns>
    private static bool IsInMemoryProvider(IInboxDbContext context)
    {
        return context is DbContext dbContext
               && dbContext.Database.ProviderName == InMemoryProviderName;
    }

    /// <summary>
    ///     Returns whether the context uses the Npgsql provider.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <returns><see langword="true" /> when the Npgsql provider is active.</returns>
    private static bool IsNpgsqlProvider(IInboxDbContext context)
    {
        return context is DbContext dbContext
               && dbContext.Database.ProviderName == NpgsqlProviderName;
    }

    /// <summary>
    ///     Builds a quoted schema-qualified table name for PostgreSQL SQL.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The quoted qualified table name.</returns>
    private static string QualifyTableName(string schemaName, string tableName)
    {
        return $"\"{schemaName}\".\"{tableName}\"";
    }

    /// <summary>
    ///     Maps a persisted entity to an inbox envelope.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>The mapped envelope.</returns>
    private static InboxEnvelope ToEnvelope(InboxMessageEntity entity)
    {
        return new InboxEnvelope
        {
            Id = entity.Id,
            ContractName = entity.ContractName,
            ContractVersion = entity.ContractVersion,
            Payload = entity.Payload,
            CreatedAt = entity.CreatedAt,
            VisibleAfter = entity.VisibleAfter,
            AttemptCount = entity.AttemptCount,
            Status = entity.Status,
            IdempotencyKey = entity.IdempotencyKey,
            LeaseOwner = entity.LeaseOwner,
            LeaseExpiresAt = entity.LeaseExpiresAt,
            LastError = entity.LastError,
            CorrelationId = entity.CorrelationId,
            CausationId = entity.CausationId,
            TenantId = entity.TenantId
        };
    }

    /// <summary>
    ///     Maps a lease SQL row to an inbox envelope.
    /// </summary>
    /// <param name="row">The row returned by PostgreSQL lease SQL.</param>
    /// <returns>The mapped envelope.</returns>
    private static InboxEnvelope ToEnvelope(InboxMessageLeaseRow row)
    {
        return new InboxEnvelope
        {
            Id = row.Id,
            ContractName = row.ContractName,
            ContractVersion = row.ContractVersion,
            Payload = row.Payload,
            CreatedAt = row.CreatedAt,
            VisibleAfter = row.VisibleAfter,
            AttemptCount = row.AttemptCount,
            Status = (InboxStatus)row.Status,
            IdempotencyKey = row.IdempotencyKey,
            LeaseOwner = row.LeaseOwner,
            LeaseExpiresAt = row.LeaseExpiresAt,
            LastError = row.LastError,
            CorrelationId = row.CorrelationId,
            CausationId = row.CausationId,
            TenantId = row.TenantId
        };
    }

    /// <summary>
    ///     Maps an inbox envelope to a persistence entity for insert.
    /// </summary>
    /// <param name="envelope">The envelope to map.</param>
    /// <returns>The mapped entity.</returns>
    private static InboxMessageEntity ToEntity(InboxEnvelope envelope)
    {
        return new InboxMessageEntity
        {
            Id = envelope.Id,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            Payload = envelope.Payload,
            CreatedAt = envelope.CreatedAt,
            VisibleAfter = envelope.VisibleAfter,
            AttemptCount = envelope.AttemptCount,
            Status = envelope.Status,
            IdempotencyKey = envelope.IdempotencyKey,
            LeaseOwner = envelope.LeaseOwner,
            LeaseExpiresAt = envelope.LeaseExpiresAt,
            LastError = envelope.LastError,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId
        };
    }

    /// <summary>
    ///     Represents one row returned by PostgreSQL lease SQL.
    /// </summary>
    private sealed class InboxMessageLeaseRow
    {
        /// <summary>
        ///     Gets or sets the command identifier column.
        /// </summary>
        [Column("command_id")]
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the contract name column.
        /// </summary>
        [Column("contract_name")]
        public string ContractName { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the contract version column.
        /// </summary>
        [Column("contract_version")]
        public int ContractVersion { get; set; }

        /// <summary>
        ///     Gets or sets the payload column.
        /// </summary>
        [Column("payload")]
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the created timestamp column.
        /// </summary>
        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        ///     Gets or sets the visible-after column.
        /// </summary>
        [Column("visible_after")]
        public DateTimeOffset? VisibleAfter { get; set; }

        /// <summary>
        ///     Gets or sets the attempt count column.
        /// </summary>
        [Column("attempt_count")]
        public int AttemptCount { get; set; }

        /// <summary>
        ///     Gets or sets the status column stored as an integer.
        /// </summary>
        [Column("status")]
        public int Status { get; set; }

        /// <summary>
        ///     Gets or sets the idempotency key column.
        /// </summary>
        [Column("idempotency_key")]
        public string? IdempotencyKey { get; set; }

        /// <summary>
        ///     Gets or sets the lease owner column.
        /// </summary>
        [Column("lease_owner")]
        public string? LeaseOwner { get; set; }

        /// <summary>
        ///     Gets or sets the lease expiration column.
        /// </summary>
        [Column("lease_expires_at")]
        public DateTimeOffset? LeaseExpiresAt { get; set; }

        /// <summary>
        ///     Gets or sets the last error column.
        /// </summary>
        [Column("last_error")]
        public string? LastError { get; set; }

        /// <summary>
        ///     Gets or sets the correlation identifier column.
        /// </summary>
        [Column("correlation_id")]
        public string? CorrelationId { get; set; }

        /// <summary>
        ///     Gets or sets the causation identifier column.
        /// </summary>
        [Column("causation_id")]
        public string? CausationId { get; set; }

        /// <summary>
        ///     Gets or sets the tenant identifier column.
        /// </summary>
        [Column("tenant_id")]
        public string? TenantId { get; set; }
    }
}
