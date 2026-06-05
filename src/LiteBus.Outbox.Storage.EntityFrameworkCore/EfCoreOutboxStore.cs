using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core outbox store that implements writer, lease, and state roles.
/// </summary>
/// <remarks>
///     <para>
///         Relational providers use provider-specific skip-locked lease SQL for PostgreSQL, SQL Server, and MySQL.
///         The in-memory and SQLite providers use a process-wide lock with translatable queries and the same
///         visibility rules so unit tests and local SQLite deployments can run without skip-locked SQL.
///     </para>
///     <para>
///         Applications own the <see cref="DbContext" /> and migrations. Call
///         <see cref="OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> from
///         <c>OnModelCreating</c> to align schema with this store.
///     </para>
///     <para>
///         The default <see cref="IOutboxStore" /> registration resolves a scoped <see cref="DbContext" /> per store call
///         and commits outbox rows immediately inside <see cref="AddAsync(OutboxEnvelope, CancellationToken)" />. To
///         participate in the caller's unit of work, call
///         <see cref="UseExistingDbContext{TContext}(TContext)" /> or stage envelopes with
///         <see cref="LiteBusOutboxSaveChangesInterceptor" /> before <c>SaveChanges</c>.
///     </para>
/// </remarks>
public sealed class EfCoreOutboxStore :
    IOutboxStore,
    IOutboxLeaseStore,
    IOutboxTerminalStateStore,
    IOutboxRetentionStore,
    IOutboxDiagnosticsStore,
    ITransactionalOutboxStore,
    IAsyncDisposable
{
    /// <summary>
    ///     Serializes in-memory and SQLite outbox leasing when multiple workers run in one process.
    /// </summary>
    private readonly SemaphoreSlim _inMemoryLeaseLock = new(1, 1);

    /// <summary>
    ///     Resolves a database context for direct factory construction used in tests.
    /// </summary>
    private readonly Func<CancellationToken, Task<IOutboxDbContext>>? _contextFactory;

    /// <summary>
    ///     The Entity Framework Core context type registered for scoped resolution.
    /// </summary>
    private readonly Type? _dbContextType;

    /// <summary>
    ///     The existing database context used when callers need to participate in an outer transaction.
    /// </summary>
    private readonly IOutboxDbContext? _existingContext;

    /// <summary>
    ///     Store options that define schema and table names for raw SQL leasing.
    /// </summary>
    private readonly EfCoreOutboxStoreOptions _options;

    /// <summary>
    ///     Gets a value indicating whether add operations call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)" /> immediately.
    /// </summary>
    private readonly bool _saveChangesOnAdd = true;

    /// <summary>
    ///     Creates scopes that resolve application database contexts from dependency injection.
    /// </summary>
    private readonly IServiceScopeFactory? _scopeFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStore" /> class using dependency injection scopes.
    /// </summary>
    /// <param name="scopeFactory">The scope factory that resolves the application database context.</param>
    /// <param name="dbContextType">The database context type that implements <see cref="IOutboxDbContext" />.</param>
    /// <param name="options">The store options.</param>
    public EfCoreOutboxStore(
        IServiceScopeFactory scopeFactory,
        Type dbContextType,
        EfCoreOutboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(dbContextType);
        ArgumentNullException.ThrowIfNull(options);

        if (!typeof(IOutboxDbContext).IsAssignableFrom(dbContextType))
        {
            throw new ArgumentException(
                $"The database context type '{dbContextType.FullName}' must implement {nameof(IOutboxDbContext)}.",
                nameof(dbContextType));
        }

        _scopeFactory = scopeFactory;
        _dbContextType = dbContextType;
        _options = options;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStore" /> class using a custom context factory.
    /// </summary>
    /// <param name="contextFactory">A factory that returns a context for one store operation.</param>
    /// <param name="options">The store options.</param>
    public EfCoreOutboxStore(
        Func<CancellationToken, Task<IOutboxDbContext>> contextFactory,
        EfCoreOutboxStoreOptions options)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStore" /> class bound to an existing context.
    /// </summary>
    /// <param name="context">The existing outbox database context.</param>
    /// <param name="options">The store options.</param>
    /// <param name="saveChangesOnAdd">
    ///     <see langword="true" /> to call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)" /> inside
    ///     <see cref="AddAsync(OutboxEnvelope, CancellationToken)" />; otherwise, <see langword="false" />.
    /// </param>
    private EfCoreOutboxStore(
        IOutboxDbContext context,
        EfCoreOutboxStoreOptions options,
        bool saveChangesOnAdd)
    {
        _existingContext = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _saveChangesOnAdd = saveChangesOnAdd;
    }

    /// <summary>
    ///     Returns a store instance that writes through an existing application <see cref="DbContext" />.
    /// </summary>
    /// <typeparam name="TContext">The concrete context type.</typeparam>
    /// <param name="context">The existing context that owns the ambient transaction.</param>
    /// <returns>
    ///     A store bound to <paramref name="context" /> where <see cref="AddAsync(OutboxEnvelope, CancellationToken)" />
    ///     stages inserts and defers commit to the caller's <c>SaveChanges</c> call.
    /// </returns>
    public ITransactionalOutboxStore UseExistingDbContext<TContext>(TContext context)
        where TContext : DbContext, IOutboxDbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        return new EfCoreOutboxStore(context, _options, saveChangesOnAdd: false);
    }

    /// <summary>
    ///     Returns a store bound to the supplied outbox database context.
    /// </summary>
    /// <param name="context">The context that owns the ambient transaction.</param>
    /// <returns>
    ///     A writer where <see cref="AddAsync(OutboxEnvelope, CancellationToken)" /> stages rows until the caller invokes
    ///     <c>SaveChanges</c> on <paramref name="context" />.
    /// </returns>
    public ITransactionalOutboxStore BindToContext(IOutboxDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not DbContext)
        {
            throw new ArgumentException(
                $"The supplied context must inherit from {nameof(DbContext)}.",
                nameof(context));
        }

        return new EfCoreOutboxStore(context, _options, saveChangesOnAdd: false);
    }

    /// <inheritdoc />
    public async Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return await ExecuteAsync(async (context, token) =>
        {
            var local = context.OutboxMessages.Local.SingleOrDefault(message => message.Id == envelope.Id);
            if (local is not null)
            {
                return ToEnvelope(local);
            }

            var existing = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token).ConfigureAwait(false);
            if (existing is not null)
            {
                return ToEnvelope(existing);
            }

            var entity = ToEntity(envelope);
            context.OutboxMessages.Add(entity);

            if (!_saveChangesOnAdd)
            {
                return ToEnvelope(entity);
            }

            try
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
                return ToEnvelope(entity);
            }
            catch (DbUpdateException)
            {
                var stored = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token).ConfigureAwait(false);

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
    public async Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
        OutboxLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Leasing requires a DbContext-backed outbox database context.");
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
    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.OutboxMessages
                .SingleOrDefaultAsync(message => message.Id == messageId, token)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            ApplyMutableState(entity, ToEnvelope(entity).AsPublished());
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(OutboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.OutboxMessages
                .SingleOrDefaultAsync(message => message.Id == failure.Id, token)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            ApplyMutableState(entity, ToEnvelope(entity).AsFailed(failure.Error, failure.VisibleAfter));
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(OutboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        return ExecuteAsync(async (context, token) =>
        {
            var entity = await context.OutboxMessages
                .SingleOrDefaultAsync(message => message.Id == deadLetter.Id, token)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            ApplyMutableState(entity, ToEnvelope(entity).AsDeadLettered(deadLetter.Reason));
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkPublishedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        return ExecuteAsync(async (context, token) =>
        {
            var entities = await context.OutboxMessages
                .Where(message => messageIds.Contains(message.Id))
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var entity in entities)
            {
                ApplyMutableState(entity, ToEnvelope(entity).AsPublished());
            }

            if (entities.Count > 0)
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(IReadOnlyList<OutboxEnvelopeFailure> failures, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return ExecuteAsync(async (context, token) =>
        {
            var failureById = failures.ToDictionary(failure => failure.Id);

            var entities = await context.OutboxMessages
                .Where(message => failureById.Keys.Contains(message.Id))
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var entity in entities)
            {
                var failure = failureById[entity.Id];
                ApplyMutableState(entity, ToEnvelope(entity).AsFailed(failure.Error, failure.VisibleAfter));
            }

            if (entities.Count > 0)
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(IReadOnlyList<OutboxEnvelopeDeadLetter> deadLetters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetters);

        return ExecuteAsync(async (context, token) =>
        {
            if (deadLetters.Count == 0)
            {
                return;
            }

            var deadLetterById = deadLetters.ToDictionary(deadLetter => deadLetter.Id);

            var entities = await context.OutboxMessages
                .Where(message => deadLetterById.Keys.Contains(message.Id))
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var entity in entities)
            {
                var deadLetter = deadLetterById[entity.Id];
                ApplyMutableState(entity, ToEnvelope(entity).AsDeadLettered(deadLetter.Reason));
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
            var entity = await context.OutboxMessages
                .SingleOrDefaultAsync(message => message.Id == messageId, token)
                .ConfigureAwait(false);

            if (entity is null || entity.Status != OutboxStatus.DeadLettered)
            {
                return;
            }

            ApplyMutableState(entity, ToEnvelope(entity).AsRequeued());
            await SaveChangesAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RequeueDeadLetterAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        return ExecuteAsync(async (context, token) =>
        {
            if (messageIds.Count == 0)
            {
                return;
            }

            var entities = await context.OutboxMessages
                .Where(message => messageIds.Contains(message.Id) && message.Status == OutboxStatus.DeadLettered)
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var entity in entities)
            {
                ApplyMutableState(entity, ToEnvelope(entity).AsRequeued());
            }

            if (entities.Count > 0)
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Retention cleanup requires a DbContext-backed outbox database context.");
            }

            return await dbContext.Set<OutboxMessageEntity>()
                .Where(message => message.Status == OutboxStatus.Published && message.CreatedAt < olderThan)
                .ExecuteDeleteAsync(token)
                .ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            var counts = await context.OutboxMessages
                .GroupBy(message => message.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(token)
                .ConfigureAwait(false);

            return (IReadOnlyDictionary<OutboxStatus, int>)counts.ToDictionary(entry => entry.Status, entry => entry.Count);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _inMemoryLeaseLock.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Runs one store operation against a resolved database context.
    /// </summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="action">The action that uses the context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The action result.</returns>
    private async Task<TResult> ExecuteAsync<TResult>(
        Func<IOutboxDbContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        if (_existingContext is not null)
        {
            return await action(_existingContext, cancellationToken).ConfigureAwait(false);
        }

        if (_contextFactory is not null)
        {
            var context = await _contextFactory(cancellationToken).ConfigureAwait(false);
            return await action(context, cancellationToken).ConfigureAwait(false);
        }

        if (_scopeFactory is null || _dbContextType is null)
        {
            throw new InvalidOperationException("The outbox store is not configured with a context factory or scope factory.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var contextFromScope = (IOutboxDbContext)scope.ServiceProvider.GetRequiredService(_dbContextType);
        return await action(contextFromScope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs one store operation that does not return a value.
    /// </summary>
    /// <param name="action">The action that uses the context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private Task ExecuteAsync(
        Func<IOutboxDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async (context, token) =>
        {
            await action(context, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    ///     Leases messages using a supported relational provider dialect.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="provider">The resolved storage provider.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased message envelopes.</returns>
    private async Task<IReadOnlyList<OutboxEnvelope>> LeasePendingRelationalAsync(
        DbContext dbContext,
        EfCoreStorageProvider provider,
        OutboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        List<OutboxMessageLeaseRow> rows = provider switch
        {
            EfCoreStorageProvider.PostgreSql => await EfCoreRelationalLeaseExecutor
                .LeasePostgreSqlAsync<OutboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Outbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int)OutboxStatus.Pending,
                    (int)OutboxStatus.Failed,
                    request.Now,
                    (int)OutboxStatus.Publishing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    cancellationToken)
                .ConfigureAwait(false),
            EfCoreStorageProvider.SqlServer => await EfCoreRelationalLeaseExecutor
                .LeaseSqlServerAsync<OutboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Outbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int)OutboxStatus.Pending,
                    (int)OutboxStatus.Failed,
                    request.Now,
                    (int)OutboxStatus.Publishing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    cancellationToken)
                .ConfigureAwait(false),
            EfCoreStorageProvider.MySql => await EfCoreRelationalLeaseExecutor
                .LeaseMySqlAsync<OutboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Outbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int)OutboxStatus.Pending,
                    (int)OutboxStatus.Failed,
                    request.Now,
                    (int)OutboxStatus.Publishing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException(
                $"Outbox leasing is not supported for Entity Framework provider '{provider}'.")
        };

        return rows.Select(ToEnvelope).ToArray();
    }

    /// <summary>
    ///     Leases messages using in-memory queries guarded by a process lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased message envelopes.</returns>
    private async Task<IReadOnlyList<OutboxEnvelope>> LeasePendingInMemoryAsync(
        IOutboxDbContext context,
        OutboxLeaseRequest request,
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
    ///     Leases messages from the in-memory or SQLite provider inside the in-process lease lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased message envelopes.</returns>
    private static async Task<IReadOnlyList<OutboxEnvelope>> LeasePendingInMemoryCoreAsync(
        IOutboxDbContext context,
        OutboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var now = request.Now;
        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        var pendingStatus = OutboxStatus.Pending;
        var failedStatus = OutboxStatus.Failed;
        var publishingStatus = OutboxStatus.Publishing;

        var candidates = await context.OutboxMessages
            .Where(message =>
                (message.Status == pendingStatus || message.Status == failedStatus)
                && (message.VisibleAfter == null || message.VisibleAfter <= now)
                || message.Status == publishingStatus
                && message.LeaseExpiresAt != null
                && message.LeaseExpiresAt <= now)
            .OrderBy(message => message.CreatedAt)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in candidates)
        {
            ApplyMutableState(message, ToEnvelope(message).AsLeased(request.LeaseOwner, leaseExpiresAt));
        }

        if (context is DbContext dbContext)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return candidates.Select(ToEnvelope).ToArray();
    }

    /// <summary>
    ///     Finds an existing outbox row by message id or idempotency key.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="messageId">The message identifier from the attempted insert.</param>
    /// <param name="idempotencyKey">The optional idempotency key from the attempted insert.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The existing entity when found; otherwise, <see langword="null" />.</returns>
    private static async Task<OutboxMessageEntity?> FindExistingEntityAsync(
        IOutboxDbContext context,
        Guid messageId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await context.OutboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Id == messageId || message.IdempotencyKey == idempotencyKey)
            .OrderBy(message => message.Id == messageId ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists pending changes through the underlying <see cref="DbContext" />.
    /// </summary>
    /// <param name="context">The outbox database context.</param>
    /// <param name="cancellationToken">A token that cancels the save operation.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    private static Task SaveChangesAsync(IOutboxDbContext context, CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("The outbox database context must inherit from DbContext.");
        }

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     Detaches a failed insert entity so the context can continue tracking other rows.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="entity">The entity that failed to insert.</param>
    private static void DetachFailedInsert(IOutboxDbContext context, OutboxMessageEntity entity)
    {
        if (context is DbContext dbContext)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    /// <summary>
    ///     Maps a persisted entity to an outbox envelope.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>The mapped envelope.</returns>
    private static OutboxEnvelope ToEnvelope(OutboxMessageEntity entity)
    {
        return new OutboxEnvelope
        {
            Id = entity.Id,
            ContractName = entity.ContractName,
            ContractVersion = entity.ContractVersion,
            Payload = entity.Payload,
            Topic = entity.Topic,
            CreatedAt = entity.CreatedAt,
            VisibleAfter = entity.VisibleAfter,
            Status = entity.Status,
            AttemptCount = entity.AttemptCount,
            LeaseOwner = entity.LeaseOwner,
            LeaseExpiresAt = entity.LeaseExpiresAt,
            LastError = entity.LastError,
            CorrelationId = entity.CorrelationId,
            CausationId = entity.CausationId,
            TenantId = entity.TenantId,
            IdempotencyKey = entity.IdempotencyKey,
            TraceContext = entity.TraceContext
        };
    }

    /// <summary>
    ///     Maps a lease SQL row to an outbox envelope.
    /// </summary>
    /// <param name="row">The row returned by PostgreSQL lease SQL.</param>
    /// <returns>The mapped envelope.</returns>
    private static OutboxEnvelope ToEnvelope(OutboxMessageLeaseRow row)
    {
        return new OutboxEnvelope
        {
            Id = row.Id,
            ContractName = row.ContractName,
            ContractVersion = row.ContractVersion,
            Payload = row.Payload,
            Topic = row.Topic,
            CreatedAt = row.CreatedAt,
            VisibleAfter = row.VisibleAfter,
            Status = (OutboxStatus)row.Status,
            AttemptCount = row.AttemptCount,
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
    ///     Maps an outbox envelope to a persistence entity for insert.
    /// </summary>
    /// <param name="envelope">The envelope to map.</param>
    /// <returns>The mapped entity.</returns>
    private static OutboxMessageEntity ToEntity(OutboxEnvelope envelope)
    {
        return new OutboxMessageEntity
        {
            Id = envelope.Id,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            Payload = envelope.Payload,
            Topic = envelope.Topic,
            CreatedAt = envelope.CreatedAt,
            VisibleAfter = envelope.VisibleAfter,
            Status = envelope.Status,
            AttemptCount = envelope.AttemptCount,
            LeaseOwner = envelope.LeaseOwner,
            LeaseExpiresAt = envelope.LeaseExpiresAt,
            LastError = envelope.LastError,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId,
            IdempotencyKey = envelope.IdempotencyKey,
            TraceContext = envelope.TraceContext
        };
    }

    /// <summary>
    ///     Copies lease, status, and error fields from an envelope transition onto a tracked entity.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="envelope">The envelope produced by a transition method.</param>
    private static void ApplyMutableState(OutboxMessageEntity entity, OutboxEnvelope envelope)
    {
        entity.Status = envelope.Status;
        entity.VisibleAfter = envelope.VisibleAfter;
        entity.AttemptCount = envelope.AttemptCount;
        entity.LeaseOwner = envelope.LeaseOwner;
        entity.LeaseExpiresAt = envelope.LeaseExpiresAt;
        entity.LastError = envelope.LastError;
    }

    /// <summary>
    ///     Represents one row returned by relational lease SQL.
    /// </summary>
    private sealed class OutboxMessageLeaseRow
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
        ///     Gets or sets the topic column.
        /// </summary>
        [Column("topic")]
        public string? Topic { get; set; }

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
        ///     Gets or sets the status column stored as an integer.
        /// </summary>
        [Column("status")]
        public int Status { get; set; }

        /// <summary>
        ///     Gets or sets the attempt count column.
        /// </summary>
        [Column("attempt_count")]
        public int AttemptCount { get; set; }

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
