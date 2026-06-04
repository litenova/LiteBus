using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core inbox store that implements writer, lease, and state roles.
/// </summary>
/// <remarks>
///     <para>
///         Relational providers use provider-specific skip-locked lease SQL for PostgreSQL, SQL Server, and MySQL.
///         The in-memory and SQLite providers use a process-wide lock with translatable queries and the same
///         visibility rules so unit tests and local SQLite deployments can run without skip-locked SQL.
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
    ///     Serializes in-memory and SQLite inbox leasing when multiple workers run in one process.
    /// </summary>
    private readonly SemaphoreSlim _inMemoryLeaseLock = new(1, 1);

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
                return ToEnvelope(entity);
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
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Leasing requires a DbContext-backed inbox database context.");
            }

            var provider = EfCoreProviderResolver.Resolve(dbContext, _options.LeaseProvider);

            if (provider is EfCoreStorageProvider.InMemory or EfCoreStorageProvider.Sqlite)
            {
                return await LeasePendingInMemoryAsync(context, request, token).ConfigureAwait(false);
            }

            return await LeasePendingRelationalAsync(dbContext, provider, request, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.InboxMessages
                .SingleOrDefaultAsync(message => message.Id == messageId, token)
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

    /// <inheritdoc />
    public Task MarkCompletedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        return ExecuteAsync(async (context, token) =>
        {
            var entities = await context.InboxMessages
                .Where(message => messageIds.Contains(message.Id))
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var entity in entities)
            {
                entity.Status = InboxStatus.Completed;
                entity.LeaseOwner = null;
                entity.LeaseExpiresAt = null;
                entity.LastError = null;
            }

            if (entities.Count > 0)
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(IReadOnlyList<InboxEnvelopeFailure> failures, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return ExecuteAsync(async (context, token) =>
        {
            var failureById = failures.ToDictionary(failure => failure.Id);

            var entities = await context.InboxMessages
                .Where(message => failureById.Keys.Contains(message.Id))
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var entity in entities)
            {
                var failure = failureById[entity.Id];
                entity.Status = InboxStatus.Failed;
                entity.VisibleAfter = failure.VisibleAfter;
                entity.LeaseOwner = null;
                entity.LeaseExpiresAt = null;
                entity.LastError = failure.Error;
            }

            if (entities.Count > 0)
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.InboxMessages
                .SingleOrDefaultAsync(message => message.Id == messageId, token)
                .ConfigureAwait(false);

            if (entity is null || entity.Status != InboxStatus.DeadLettered)
            {
                return;
            }

            entity.Status = InboxStatus.Pending;
            entity.VisibleAfter = null;
            entity.AttemptCount = 0;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = null;
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Retention cleanup requires a DbContext-backed inbox database context.");
            }

            return await dbContext.Set<InboxMessageEntity>()
                .Where(message => message.Status == InboxStatus.Completed && message.CreatedAt < olderThan)
                .ExecuteDeleteAsync(token)
                .ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            var counts = await context.InboxMessages
                .GroupBy(message => message.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(token)
                .ConfigureAwait(false);

            return (IReadOnlyDictionary<InboxStatus, int>)counts.ToDictionary(
                entry => entry.Status,
                entry => entry.Count);
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
    ///     Leases commands using a supported relational provider dialect.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="provider">The resolved storage provider.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased command envelopes.</returns>
    private async Task<IReadOnlyList<InboxEnvelope>> LeasePendingRelationalAsync(
        DbContext dbContext,
        EfCoreStorageProvider provider,
        InboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        List<InboxMessageLeaseRow> rows = provider switch
        {
            EfCoreStorageProvider.PostgreSql => await EfCoreRelationalLeaseExecutor
                .LeasePostgreSqlAsync<InboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Inbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int)InboxStatus.Pending,
                    (int)InboxStatus.Failed,
                    request.Now,
                    (int)InboxStatus.Processing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    cancellationToken)
                .ConfigureAwait(false),
            EfCoreStorageProvider.SqlServer => await EfCoreRelationalLeaseExecutor
                .LeaseSqlServerAsync<InboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Inbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int)InboxStatus.Pending,
                    (int)InboxStatus.Failed,
                    request.Now,
                    (int)InboxStatus.Processing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    cancellationToken)
                .ConfigureAwait(false),
            EfCoreStorageProvider.MySql => await EfCoreRelationalLeaseExecutor
                .LeaseMySqlAsync<InboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Inbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int)InboxStatus.Pending,
                    (int)InboxStatus.Failed,
                    request.Now,
                    (int)InboxStatus.Processing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException(
                $"Inbox leasing is not supported for Entity Framework provider '{provider}'.")
        };

        return rows.Select(ToEnvelope).ToArray();
    }

    /// <summary>
    ///     Leases commands using in-memory queries guarded by a process lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased command envelopes.</returns>
    private async Task<IReadOnlyList<InboxEnvelope>> LeasePendingInMemoryAsync(
        IInboxDbContext context,
        InboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        await _inMemoryLeaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await LeasePendingInMemoryCoreAsync(context, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inMemoryLeaseLock.Release();
        }
    }

    /// <summary>
    ///     Leases commands from the in-memory or SQLite provider inside the in-process lease lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased command envelopes.</returns>
    private static async Task<IReadOnlyList<InboxEnvelope>> LeasePendingInMemoryCoreAsync(
        IInboxDbContext context,
        InboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var now = request.Now;
        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        var pendingStatus = InboxStatus.Pending;
        var failedStatus = InboxStatus.Failed;
        var processingStatus = InboxStatus.Processing;

        var candidates = await context.InboxMessages
            .Where(message =>
                (message.Status == pendingStatus || message.Status == failedStatus)
                && (message.VisibleAfter == null || message.VisibleAfter <= now)
                || message.Status == processingStatus
                && message.LeaseExpiresAt != null
                && message.LeaseExpiresAt <= now)
            .OrderBy(message => message.CreatedAt)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in candidates)
        {
            message.Status = InboxStatus.Processing;
            message.LeaseOwner = request.LeaseOwner;
            message.LeaseExpiresAt = leaseExpiresAt;
            message.AttemptCount++;
        }

        if (context is DbContext dbContext)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return candidates.Select(ToEnvelope).ToArray();
    }

    /// <summary>
    ///     Finds an existing row after a duplicate insert attempt.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="messageId">The message identifier from the attempted insert.</param>
    /// <param name="idempotencyKey">The idempotency key from the attempted insert.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The existing entity when found; otherwise, <see langword="null" />.</returns>
    private static async Task<InboxMessageEntity?> FindExistingEntityAsync(
        IInboxDbContext context,
        Guid messageId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await context.InboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await context.InboxMessages
            .AsNoTracking()
            .Where(message => message.Id == messageId || message.IdempotencyKey == idempotencyKey)
            .OrderBy(message => message.Id == messageId ? 0 : 1)
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
    ///     Detaches a failed insert entity so the context can continue tracking other rows.
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
            TenantId = entity.TenantId,
            TraceContext = entity.TraceContext
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
            TenantId = row.TenantId,
            TraceContext = row.TraceContext
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
            TenantId = envelope.TenantId,
            TraceContext = envelope.TraceContext
        };
    }

    /// <summary>
    ///     Represents one row returned by relational lease SQL.
    /// </summary>
    private sealed class InboxMessageLeaseRow
    {
        /// <summary>
        ///     Gets or sets the message identifier column.
        /// </summary>
        [Column("message_id")]
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

        /// <summary>
        ///     Gets or sets the trace context column stored as JSON text.
        /// </summary>
        [Column("trace_context")]
        public string? TraceContext { get; set; }
    }
}
