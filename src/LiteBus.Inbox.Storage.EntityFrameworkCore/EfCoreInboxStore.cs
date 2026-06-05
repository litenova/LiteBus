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
///     <para>
///         The default <see cref="IInboxStore" /> registration resolves a scoped <see cref="DbContext" /> per store call
///         and commits inbox rows immediately inside <see cref="AddAsync(InboxEnvelope, CancellationToken)" />. To
///         participate in the caller's unit of work, call
///         <see cref="UseExistingDbContext{TContext}(TContext)" /> or stage envelopes with
///         <see cref="LiteBusInboxSaveChangesInterceptor" /> before <c>SaveChanges</c>.
///     </para>
/// </remarks>
public sealed class EfCoreInboxStore :
    IInboxStore,
    IInboxLeaseStore,
    IInboxStateWriter,
    IInboxDeadLetterStore,
    IInboxRetentionStore,
    IInboxDiagnosticsStore,
    IInboxMessageQuery,
    IInboxPurgeStore,
    ITransactionalInboxStore,
    IAsyncDisposable
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
    ///     The existing database context used when callers need to participate in an outer transaction.
    /// </summary>
    private readonly IInboxDbContext? _existingContext;

    /// <summary>
    ///     Store options that define schema and table names for raw SQL leasing.
    /// </summary>
    private readonly EfCoreInboxStoreOptions _options;

    /// <summary>
    ///     Gets a value indicating whether add operations call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)" /> immediately.
    /// </summary>
    private readonly bool _saveChangesOnAdd = true;

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

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStore" /> class bound to an existing context.
    /// </summary>
    /// <param name="context">The existing inbox database context.</param>
    /// <param name="options">The store options.</param>
    /// <param name="saveChangesOnAdd">
    ///     <see langword="true" /> to call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)" /> inside
    ///     <see cref="AddAsync(InboxEnvelope, CancellationToken)" />; otherwise, <see langword="false" />.
    /// </param>
    private EfCoreInboxStore(
        IInboxDbContext context,
        EfCoreInboxStoreOptions options,
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
    ///     A store bound to <paramref name="context" /> where <see cref="AddAsync(InboxEnvelope, CancellationToken)" />
    ///     stages inserts and defers commit to the caller's <c>SaveChanges</c> call.
    /// </returns>
    public ITransactionalInboxStore UseExistingDbContext<TContext>(TContext context)
        where TContext : DbContext, IInboxDbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        return new EfCoreInboxStore(context, _options, saveChangesOnAdd: false);
    }

    /// <inheritdoc />
    public async Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return await ExecuteAsync(async (context, token) =>
        {
            var local = context.InboxMessages.Local.SingleOrDefault(message => message.Id == envelope.Id);
            if (local is not null)
            {
                return ToEnvelope(local);
            }

            var existing = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return ToEnvelope(existing);
            }

            var entity = ToEntity(envelope);
            context.InboxMessages.Add(entity);

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
    public Task PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Task.CompletedTask;
        }

        return ExecuteAsync(async (context, token) =>
        {
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

            var changed = false;

            if (completed is not null)
            {
                changed |= await ApplyCompletedAsync(context, completed, token).ConfigureAwait(false);
            }

            if (failed is not null)
            {
                changed |= await ApplyFailedAsync(context, failed, token).ConfigureAwait(false);
            }

            if (deadLettered is not null)
            {
                changed |= await ApplyDeadLetteredAsync(context, deadLettered, token).ConfigureAwait(false);
            }

            if (changed)
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <summary>
    ///     Applies completed status for one or more envelopes.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The completed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns><see langword="true" /> when at least one entity was updated.</returns>
    private static async Task<bool> ApplyCompletedAsync(
        IInboxDbContext context,
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var envelopeById = envelopes.ToDictionary(envelope => envelope.Id);
        var entities = await context.InboxMessages
            .Where(message => envelopeById.Keys.Contains(message.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entity in entities)
        {
            var source = envelopeById[entity.Id];
            ApplyMutableState(entity, source.Status == InboxStatus.Completed
                ? source
                : source.AsCompleted());
        }

        return entities.Count > 0;
    }

    /// <summary>
    ///     Applies failed status and retry metadata for one or more envelopes.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The failed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns><see langword="true" /> when at least one entity was updated.</returns>
    private static async Task<bool> ApplyFailedAsync(
        IInboxDbContext context,
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var envelopeById = envelopes.ToDictionary(envelope => envelope.Id);
        var entities = await context.InboxMessages
            .Where(message => envelopeById.Keys.Contains(message.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entity in entities)
        {
            ApplyMutableState(entity, envelopeById[entity.Id]);
        }

        return entities.Count > 0;
    }

    /// <summary>
    ///     Applies dead-letter status for one or more envelopes.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The dead-lettered envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns><see langword="true" /> when at least one entity was updated.</returns>
    private static async Task<bool> ApplyDeadLetteredAsync(
        IInboxDbContext context,
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var envelopeById = envelopes.ToDictionary(envelope => envelope.Id);
        var entities = await context.InboxMessages
            .Where(message => envelopeById.Keys.Contains(message.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entity in entities)
        {
            ApplyMutableState(entity, envelopeById[entity.Id]);
        }

        return entities.Count > 0;
    }

    /// <inheritdoc />
    public Task RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        return ExecuteAsync(async (context, token) =>
        {
            if (messageIds.Count == 0)
            {
                return;
            }

            var entities = await context.InboxMessages
                .Where(message => messageIds.Contains(message.Id) && message.Status == InboxStatus.DeadLettered)
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
    public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Retention cleanup requires a DbContext-backed inbox database context.");
            }

            return await dbContext.Set<InboxMessageEntity>()
                .Where(message => message.Status == InboxStatus.Completed &&
                                  (message.CompletedAt ?? message.CreatedAt) < olderThan)
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

    /// <inheritdoc />
    public Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pageRequest);
        ValidatePageSize(pageRequest.PageSize);

        return ExecuteAsync(async (context, token) =>
        {
            var query = ApplyFilter(context.InboxMessages.AsQueryable(), filter);
            query = ApplyCursor(query, pageRequest.Cursor);

            var entities = await query
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Take(pageRequest.PageSize + 1)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return BuildPage(entities.Select(ToEnvelope).ToList(), pageRequest.PageSize);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Purge requires a DbContext-backed inbox database context.");
            }

            var query = ApplyFilter(context.InboxMessages.AsQueryable(), filter);
            return await DeleteMatchingAsync(dbContext, query, token).ConfigureAwait(false);
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
        Func<IInboxDbContext, CancellationToken, Task<TResult>> action,
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
            ApplyMutableState(message, ToEnvelope(message).AsLeased(request.LeaseOwner, leaseExpiresAt));
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
            TraceContext = entity.TraceContext,
            CompletedAt = entity.CompletedAt
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
    ///     Copies lease, status, and error fields from an envelope transition onto a tracked entity.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="envelope">The envelope produced by a transition method.</param>
    private static void ApplyMutableState(InboxMessageEntity entity, InboxEnvelope envelope)
    {
        entity.Status = envelope.Status;
        entity.VisibleAfter = envelope.VisibleAfter;
        entity.AttemptCount = envelope.AttemptCount;
        entity.LeaseOwner = envelope.Status is InboxStatus.Completed or InboxStatus.Failed or InboxStatus.DeadLettered
            ? null
            : envelope.LeaseOwner;
        entity.LeaseExpiresAt = envelope.Status is InboxStatus.Completed or InboxStatus.Failed or InboxStatus.DeadLettered
            ? null
            : envelope.LeaseExpiresAt;
        entity.LastError = envelope.LastError;

        if (envelope.Status == InboxStatus.Completed)
        {
            entity.CompletedAt = envelope.CompletedAt ?? DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    ///     Applies optional inbox message filters to an Entity Framework query.
    /// </summary>
    /// <param name="query">The inbox messages to filter.</param>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <returns>The filtered query.</returns>
    private static IQueryable<InboxMessageEntity> ApplyFilter(
        IQueryable<InboxMessageEntity> query,
        InboxMessageFilter filter)
    {
        var statusValues = MaterializeStatusFilter(filter.Statuses);

        if (statusValues is not null)
        {
            query = query.Where(message => statusValues.Contains((int)message.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.ContractName))
        {
            query = query.Where(message => message.ContractName == filter.ContractName);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            query = query.Where(message => message.CorrelationId == filter.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.CausationId))
        {
            query = query.Where(message => message.CausationId == filter.CausationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.TenantId))
        {
            query = query.Where(message => message.TenantId == filter.TenantId);
        }

        if (filter.CreatedAfter is not null)
        {
            query = query.Where(message => message.CreatedAt >= filter.CreatedAfter);
        }

        if (filter.CreatedBefore is not null)
        {
            query = query.Where(message => message.CreatedAt <= filter.CreatedBefore);
        }

        return query;
    }

    /// <summary>
    ///     Applies keyset pagination to an Entity Framework query.
    /// </summary>
    /// <param name="query">The inbox messages to page.</param>
    /// <param name="cursor">The opaque cursor from a previous page.</param>
    /// <returns>The query positioned after the supplied cursor.</returns>
    private static IQueryable<InboxMessageEntity> ApplyCursor(
        IQueryable<InboxMessageEntity> query,
        string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return query;
        }

        if (!InboxMessagePageCursor.TryDecode(cursor, out var cursorCreatedAt, out var cursorMessageId))
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor));
        }

        return query.Where(message =>
            message.CreatedAt > cursorCreatedAt ||
            (message.CreatedAt == cursorCreatedAt && message.Id > cursorMessageId));
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
    ///     Deletes rows matched by a filtered query, using bulk delete when the provider supports it.
    /// </summary>
    /// <typeparam name="TEntity">The inbox entity type.</typeparam>
    /// <param name="dbContext">The database context executing the delete.</param>
    /// <param name="query">The filtered entity query.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static async Task<int> DeleteMatchingAsync<TEntity>(
        DbContext dbContext,
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (dbContext.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true)
        {
            var matches = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            if (matches.Count == 0)
            {
                return 0;
            }

            dbContext.Set<TEntity>().RemoveRange(matches);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return matches.Count;
        }

        return await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Copies status filters into a primitive array that Entity Framework can translate.
    /// </summary>
    /// <param name="statuses">The optional status filter values.</param>
    /// <returns>The copied status values, or <see langword="null" /> when status is not filtered.</returns>
    private static int[]? MaterializeStatusFilter(IReadOnlyList<InboxStatus>? statuses)
    {
        if (statuses is not { Count: > 0 })
        {
            return null;
        }

        var values = new int[statuses.Count];

        for (var index = 0; index < statuses.Count; index++)
        {
            values[index] = (int)statuses[index];
        }

        return values;
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
