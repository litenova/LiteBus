using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore.Leasing;
using LiteBus.Storage.EntityFrameworkCore.Stores;
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
    IOutboxProcessingStore,
    IOutboxOperationsStore,
    ITransactionalOutboxStore,
    IAsyncDisposable
{
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
    ///     Serializes in-memory and SQLite outbox leasing when multiple workers run in one process.
    /// </summary>
    private readonly SemaphoreSlim _inMemoryLeaseLock = new(1, 1);

    /// <summary>
    ///     Store options that define schema and table names for raw SQL leasing.
    /// </summary>
    private readonly EntityFrameworkCoreOutboxStoreOptions _options;

    /// <summary>
    ///     Gets a value indicating whether add operations call
    ///     <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)" /> immediately.
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
        EntityFrameworkCoreOutboxStoreOptions options)
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
        EntityFrameworkCoreOutboxStoreOptions options)
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
    ///     <see langword="true" /> to call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)" />
    ///     inside
    ///     <see cref="AddAsync(OutboxEnvelope, CancellationToken)" />; otherwise, <see langword="false" />.
    /// </param>
    private EfCoreOutboxStore(
        IOutboxDbContext context,
        EntityFrameworkCoreOutboxStoreOptions options,
        bool saveChangesOnAdd)
    {
        _existingContext = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _saveChangesOnAdd = saveChangesOnAdd;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _inMemoryLeaseLock.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        return ExecuteAsync(async (context, token) =>
        {
            if (messageIds.Count == 0)
            {
                return new RequeueResult(0, 0);
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

            return new RequeueResult(messageIds.Count, entities.Count);
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
                .Where(message => message.Status == OutboxStatus.Published &&
                                  (message.PublishedAt ?? message.CreatedAt) < olderThan)
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

            return (IReadOnlyDictionary<OutboxStatus, int>) counts.ToDictionary(entry => entry.Status, entry => entry.Count);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(StoreSchemaInfo.ForLogicalStore("outbox", 1));
    }

    /// <inheritdoc />
    public Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pageRequest);
        ValidatePageSize(pageRequest.PageSize);

        return ExecuteAsync(async (context, token) =>
        {
            var query = ApplyFilter(context.OutboxMessages.AsQueryable(), filter);
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
    public Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Purge requires a DbContext-backed outbox database context.");
            }

            var query = ApplyFilter(context.OutboxMessages.AsQueryable(), filter);
            return await DeleteMatchingAsync(dbContext, query, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RenewLeaseAsync(
        LeaseRenewalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);

        return ExecuteAsync(async (context, token) =>
        {
            if (context is not DbContext dbContext)
            {
                throw new InvalidOperationException("Lease renewal requires a DbContext-backed outbox database context.");
            }

            var affected = await dbContext.Set<OutboxMessageEntity>()
                .Where(message =>
                    message.Id == request.MessageId &&
                    message.Status == OutboxStatus.Publishing &&
                    message.LeaseOwner == request.LeaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(message => message.LeaseExpiresAt, request.ExpiresAt),
                    token)
                .ConfigureAwait(false);

            return affected > 0;
        }, cancellationToken);
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
    public Task<PersistResult> PersistAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Task.FromResult(PersistResult.Empty);
        }

        return ExecuteAsync(async (context, token) =>
        {
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

            var persistedMessageIds = new HashSet<Guid>();

            if (published is not null)
            {
                persistedMessageIds.UnionWith(await ApplyPublishedAsync(context, published, token).ConfigureAwait(false));
            }

            if (failed is not null)
            {
                persistedMessageIds.UnionWith(await ApplyFailedAsync(context, failed, token).ConfigureAwait(false));
            }

            if (deadLettered is not null)
            {
                persistedMessageIds.UnionWith(
                    await ApplyDeadLetteredAsync(context, deadLettered, token).ConfigureAwait(false));
            }

            if (persistedMessageIds.Count > 0 &&
                context is DbContext trackedContext &&
                trackedContext.ChangeTracker.HasChanges())
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
            }

            var messageIds = envelopes.Select(envelope => envelope.Id).ToArray();
            var appliedCount = messageIds.Count(persistedMessageIds.Contains);
            return PersistResult.FromOutcome(appliedCount, messageIds.Length - appliedCount);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return await ExecuteAsync(async (context, token) =>
        {
            var local = FindLocalEntity(context, envelope.Id, envelope.IdempotencyKey);

            if (local is not null)
            {
                return ToEnvelope(local);
            }

            var existing = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token).ConfigureAwait(false);

            if (existing is not null)
            {
                ThrowIfStrictConflict(envelope, existing);
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
                await ReloadEntityAsync(context, entity, token).ConfigureAwait(false);
                return ToEnvelope(entity);
            }
            catch (DbUpdateException)
            {
                var stored = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token).ConfigureAwait(false);

                if (stored is null)
                {
                    throw;
                }

                ThrowIfStrictConflict(envelope, stored);
                DetachFailedInsert(context, entity);
                return ToEnvelope(stored);
            }
        }, cancellationToken).ConfigureAwait(false);
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

        return await ExecuteAsync(async (context, token) =>
        {
            var results = new OutboxEnvelope[envelopes.Count];
            var pending = new List<(int Index, OutboxEnvelope Envelope)>();
            var seenIds = new Dictionary<Guid, OutboxEnvelope>();
            var seenIdempotencyKeys = new Dictionary<string, OutboxEnvelope>(StringComparer.Ordinal);
            var pendingIdempotencyKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            var pendingAliases = new Dictionary<int, int>();

            for (var index = 0; index < envelopes.Count; index++)
            {
                var envelope = envelopes[index];

                if (seenIds.TryGetValue(envelope.Id, out var duplicateById))
                {
                    results[index] = duplicateById;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey) && seenIdempotencyKeys.TryGetValue(envelope.IdempotencyKey, out var duplicateByKey))
                {
                    results[index] = duplicateByKey;
                    continue;
                }

                var local = FindLocalEntity(context, envelope.Id, envelope.IdempotencyKey);

                if (local is not null)
                {
                    results[index] = ToEnvelope(local);
                    RememberBatchResult(seenIds, seenIdempotencyKeys, envelope, results[index]);
                    continue;
                }

                var existing = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    results[index] = ToEnvelope(existing);
                    RememberBatchResult(seenIds, seenIdempotencyKeys, envelope, results[index]);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey) && pendingIdempotencyKeys.TryGetValue(envelope.IdempotencyKey, out var sourceIndex))
                {
                    pendingAliases[index] = sourceIndex;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
                {
                    pendingIdempotencyKeys[envelope.IdempotencyKey] = index;
                }

                pending.Add((index, envelope));
            }

            if (pending.Count == 0)
            {
                return results;
            }

            var entities = pending.Select(entry => ToEntity(entry.Envelope)).ToList();
            context.OutboxMessages.AddRange(entities);

            void ApplyPendingResults()
            {
                for (var index = 0; index < pending.Count; index++)
                {
                    var stored = ToEnvelope(entities[index]);
                    results[pending[index].Index] = stored;
                    RememberBatchResult(seenIds, seenIdempotencyKeys, pending[index].Envelope, stored);
                }

                foreach (var (duplicateIndex, sourceIndex) in pendingAliases)
                {
                    results[duplicateIndex] = results[sourceIndex];
                }
            }

            if (!_saveChangesOnAdd)
            {
                ApplyPendingResults();
                return results;
            }

            try
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);

                for (var index = 0; index < pending.Count; index++)
                {
                    await ReloadEntityAsync(context, entities[index], token).ConfigureAwait(false);
                }

                ApplyPendingResults();
            }
            catch (DbUpdateException)
            {
                foreach (var entity in entities)
                {
                    DetachFailedInsert(context, entity);
                }

                foreach (var (index, envelope) in pending)
                {
                    var stored = await FindExistingEntityAsync(context, envelope.Id, envelope.IdempotencyKey, token)
                                     .ConfigureAwait(false) ??
                                 throw new InvalidOperationException(
                                     "The outbox batch insert failed and the existing message could not be found.");

                    results[index] = ToEnvelope(stored);
                    RememberBatchResult(seenIds, seenIdempotencyKeys, envelope, results[index]);
                }

                foreach (var (duplicateIndex, sourceIndex) in pendingAliases)
                {
                    results[duplicateIndex] = results[sourceIndex];
                }
            }

            return results;
        }, cancellationToken).ConfigureAwait(false);
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
        return new EfCoreOutboxStore(context, _options, false);
    }

    /// <summary>
    ///     Applies published status for one or more envelopes.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The published envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated under the lease guard.</returns>
    private static async Task<HashSet<Guid>> ApplyPublishedAsync(
        IOutboxDbContext context,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Terminal persist requires a DbContext-backed outbox database context.");
        }

        if (!EfCoreBulkUpdateCapabilities.SupportsExecuteUpdate(dbContext))
        {
            return await ApplyPublishedTrackedAsync(context, envelopes, cancellationToken).ConfigureAwait(false);
        }

        var persistedMessageIds = new HashSet<Guid>();

        foreach (var envelope in envelopes)
        {
            var publishedAt = envelope.PublishedAt ?? DateTimeOffset.UtcNow;

            var affected = await dbContext.Set<OutboxMessageEntity>()
                .Where(message =>
                    message.Id == envelope.Id &&
                    message.Status == OutboxStatus.Publishing &&
                    message.LeaseOwner == envelope.LeaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.Status, OutboxStatus.Published)
                        .SetProperty(message => message.LeaseOwner, (string?) null)
                        .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?) null)
                        .SetProperty(message => message.LastError, (string?) null)
                        .SetProperty(message => message.PublishedAt, publishedAt),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                persistedMessageIds.Add(envelope.Id);
            }
        }

        return persistedMessageIds;
    }

    /// <summary>
    ///     Applies published status using tracked entities for providers without bulk update support.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The published envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated under the lease guard.</returns>
    private static async Task<HashSet<Guid>> ApplyPublishedTrackedAsync(
        IOutboxDbContext context,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var envelopeById = envelopes.ToDictionary(envelope => envelope.Id);

        var entities = await context.OutboxMessages
            .Where(message => envelopeById.Keys.Contains(message.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var persistedMessageIds = new HashSet<Guid>();

        foreach (var entity in entities)
        {
            var source = envelopeById[entity.Id];

            if (!CanApplyInFlightTerminalState(entity, source))
            {
                continue;
            }

            ApplyMutableState(entity, source.Status == OutboxStatus.Published
                ? source
                : source.AsPublished());

            persistedMessageIds.Add(entity.Id);
        }

        return persistedMessageIds;
    }

    /// <summary>
    ///     Applies failed status and retry metadata for one or more envelopes.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The failed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated under the lease guard.</returns>
    private static async Task<HashSet<Guid>> ApplyFailedAsync(
        IOutboxDbContext context,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Terminal persist requires a DbContext-backed outbox database context.");
        }

        if (!EfCoreBulkUpdateCapabilities.SupportsExecuteUpdate(dbContext))
        {
            return await ApplyFailedTrackedAsync(context, envelopes, cancellationToken).ConfigureAwait(false);
        }

        var persistedMessageIds = new HashSet<Guid>();

        foreach (var envelope in envelopes)
        {
            var affected = await dbContext.Set<OutboxMessageEntity>()
                .Where(message =>
                    message.Id == envelope.Id &&
                    message.Status == OutboxStatus.Publishing &&
                    message.LeaseOwner == envelope.LeaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.Status, OutboxStatus.Failed)
                        .SetProperty(message => message.VisibleAfter, envelope.VisibleAfter)
                        .SetProperty(message => message.AttemptCount, envelope.AttemptCount)
                        .SetProperty(message => message.LeaseOwner, (string?) null)
                        .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?) null)
                        .SetProperty(message => message.LastError, envelope.LastError),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                persistedMessageIds.Add(envelope.Id);
            }
        }

        return persistedMessageIds;
    }

    /// <summary>
    ///     Applies failed status using tracked entities for providers without bulk update support.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The failed envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated under the lease guard.</returns>
    private static async Task<HashSet<Guid>> ApplyFailedTrackedAsync(
        IOutboxDbContext context,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var envelopeById = envelopes.ToDictionary(envelope => envelope.Id);

        var entities = await context.OutboxMessages
            .Where(message => envelopeById.Keys.Contains(message.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var persistedMessageIds = new HashSet<Guid>();

        foreach (var entity in entities)
        {
            var source = envelopeById[entity.Id];

            if (!CanApplyInFlightTerminalState(entity, source))
            {
                continue;
            }

            ApplyMutableState(entity, source);
            persistedMessageIds.Add(entity.Id);
        }

        return persistedMessageIds;
    }

    /// <summary>
    ///     Applies dead-letter status for one or more envelopes.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The dead-lettered envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated under the lease guard.</returns>
    private static async Task<HashSet<Guid>> ApplyDeadLetteredAsync(
        IOutboxDbContext context,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Terminal persist requires a DbContext-backed outbox database context.");
        }

        if (!EfCoreBulkUpdateCapabilities.SupportsExecuteUpdate(dbContext))
        {
            return await ApplyDeadLetteredTrackedAsync(context, envelopes, cancellationToken).ConfigureAwait(false);
        }

        var persistedMessageIds = new HashSet<Guid>();

        foreach (var envelope in envelopes)
        {
            var affected = await dbContext.Set<OutboxMessageEntity>()
                .Where(message =>
                    message.Id == envelope.Id &&
                    message.Status == OutboxStatus.Publishing &&
                    message.LeaseOwner == envelope.LeaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.Status, OutboxStatus.DeadLettered)
                        .SetProperty(message => message.AttemptCount, envelope.AttemptCount)
                        .SetProperty(message => message.LeaseOwner, (string?) null)
                        .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?) null)
                        .SetProperty(message => message.LastError, envelope.LastError),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                persistedMessageIds.Add(envelope.Id);
            }
        }

        return persistedMessageIds;
    }

    /// <summary>
    ///     Applies dead-letter status using tracked entities for providers without bulk update support.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="envelopes">The dead-lettered envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns>The message identifiers updated under the lease guard.</returns>
    private static async Task<HashSet<Guid>> ApplyDeadLetteredTrackedAsync(
        IOutboxDbContext context,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var envelopeById = envelopes.ToDictionary(envelope => envelope.Id);

        var entities = await context.OutboxMessages
            .Where(message => envelopeById.Keys.Contains(message.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var persistedMessageIds = new HashSet<Guid>();

        foreach (var entity in entities)
        {
            var source = envelopeById[entity.Id];

            if (!CanApplyInFlightTerminalState(entity, source))
            {
                continue;
            }

            ApplyMutableState(entity, source);
            persistedMessageIds.Add(entity.Id);
        }

        return persistedMessageIds;
    }

    /// <summary>
    ///     Determines whether a terminal persist can be applied to one tracked in-flight row.
    /// </summary>
    /// <param name="entity">The tracked entity loaded from storage.</param>
    /// <param name="envelope">The post-transition envelope supplied by the processor.</param>
    /// <returns><see langword="true" /> when the row is still leased by the same owner; otherwise, <see langword="false" />.</returns>
    private static bool CanApplyInFlightTerminalState(OutboxMessageEntity entity, OutboxEnvelope envelope)
    {
        return entity.Status == OutboxStatus.Publishing &&
               string.Equals(entity.LeaseOwner, envelope.LeaseOwner, StringComparison.Ordinal);
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
        var contextFromScope = (IOutboxDbContext) scope.ServiceProvider.GetRequiredService(_dbContextType);
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
        var staleCutoff = request.Now.Add(-request.LeaseDuration);

        var rows = provider switch
        {
            EfCoreStorageProvider.PostgreSql => await EfCoreRelationalLeaseExecutor
                .LeasePostgreSqlAsync<OutboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Outbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int) OutboxStatus.Pending,
                    (int) OutboxStatus.Failed,
                    request.Now,
                    (int) OutboxStatus.Publishing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    request.TenantId,
                    staleCutoff,
                    cancellationToken)
                .ConfigureAwait(false),
            EfCoreStorageProvider.SqlServer => await EfCoreRelationalLeaseExecutor
                .LeaseSqlServerAsync<OutboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Outbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int) OutboxStatus.Pending,
                    (int) OutboxStatus.Failed,
                    request.Now,
                    (int) OutboxStatus.Publishing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    request.TenantId,
                    staleCutoff,
                    cancellationToken)
                .ConfigureAwait(false),
            EfCoreStorageProvider.MySql => await EfCoreRelationalLeaseExecutor
                .LeaseMySqlAsync<OutboxMessageLeaseRow>(
                    dbContext,
                    EfCoreLeaseComponent.Outbox,
                    provider,
                    _options.SchemaName,
                    _options.TableName,
                    (int) OutboxStatus.Pending,
                    (int) OutboxStatus.Failed,
                    request.Now,
                    (int) OutboxStatus.Publishing,
                    request.BatchSize,
                    request.LeaseOwner,
                    leaseExpiresAt,
                    request.TenantId,
                    staleCutoff,
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
        var staleCutoff = request.Now.Add(-request.LeaseDuration);
        var pendingStatus = OutboxStatus.Pending;
        var failedStatus = OutboxStatus.Failed;
        var publishingStatus = OutboxStatus.Publishing;
        var tenantId = request.TenantId;

        var candidates = await context.OutboxMessages
            .Where(message =>
                (tenantId == null || message.TenantId == tenantId) &&
                ((message.Status == pendingStatus || message.Status == failedStatus) && (message.VisibleAfter == null || message.VisibleAfter <= now) ||
                 message.Status == publishingStatus && message.LeaseExpiresAt != null && message.LeaseExpiresAt <= now ||
                 message.Status == publishingStatus && message.LeaseExpiresAt == null && message.CreatedAt < staleCutoff))
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
    ///     Finds a tracked outbox row matching the message identifier or idempotency key.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="idempotencyKey">The optional idempotency key.</param>
    /// <returns>The tracked entity when found; otherwise, <see langword="null" />.</returns>
    private static OutboxMessageEntity? FindLocalEntity(
        IOutboxDbContext context,
        Guid messageId,
        string? idempotencyKey)
    {
        return context.OutboxMessages.Local.FirstOrDefault(message =>
            message.Id == messageId || !string.IsNullOrWhiteSpace(idempotencyKey) && message.IdempotencyKey == idempotencyKey);
    }

    /// <summary>
    ///     Records one batch result so later duplicate identifiers resolve to the same envelope.
    /// </summary>
    /// <param name="seenIds">The message identifiers already resolved in the batch.</param>
    /// <param name="seenIdempotencyKeys">The idempotency keys already resolved in the batch.</param>
    /// <param name="envelope">The source envelope from the batch request.</param>
    /// <param name="result">The resolved envelope returned for the batch slot.</param>
    private static void RememberBatchResult(
        Dictionary<Guid, OutboxEnvelope> seenIds,
        Dictionary<string, OutboxEnvelope> seenIdempotencyKeys,
        OutboxEnvelope envelope,
        OutboxEnvelope result)
    {
        seenIds[envelope.Id] = result;

        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            seenIdempotencyKeys[envelope.IdempotencyKey] = result;
        }
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
    ///     Reloads a tracked entity from the database so provider-specific columns return canonical values.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="entity">The entity to reload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous reload operation.</returns>
    private static async Task ReloadEntityAsync(
        IOutboxDbContext context,
        OutboxMessageEntity entity,
        CancellationToken cancellationToken)
    {
        if (context is DbContext dbContext)
        {
            await dbContext.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
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
            TraceContext = EfCoreJsonTextNormalizer.Normalize(entity.TraceContext),
            PublishedAt = entity.PublishedAt
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
            Status = (OutboxStatus) row.Status,
            AttemptCount = row.AttemptCount,
            LeaseOwner = row.LeaseOwner,
            LeaseExpiresAt = row.LeaseExpiresAt,
            LastError = row.LastError,
            CorrelationId = row.CorrelationId,
            CausationId = row.CausationId,
            TenantId = row.TenantId,
            TraceContext = EfCoreJsonTextNormalizer.Normalize(row.TraceContext)
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

        entity.LeaseOwner = envelope.Status is OutboxStatus.Published or OutboxStatus.Failed or OutboxStatus.DeadLettered
            ? null
            : envelope.LeaseOwner;

        entity.LeaseExpiresAt = envelope.Status is OutboxStatus.Published or OutboxStatus.Failed or OutboxStatus.DeadLettered
            ? null
            : envelope.LeaseExpiresAt;

        entity.LastError = envelope.LastError;

        if (envelope.Status == OutboxStatus.Published)
        {
            entity.PublishedAt = envelope.PublishedAt ?? DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    ///     Applies optional outbox message filters to an Entity Framework query.
    /// </summary>
    /// <param name="query">The outbox messages to filter.</param>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <returns>The filtered query.</returns>
    private static IQueryable<OutboxMessageEntity> ApplyFilter(
        IQueryable<OutboxMessageEntity> query,
        OutboxMessageFilter filter)
    {
        if (filter.MessageId is not null)
        {
            query = query.Where(message => message.Id == filter.MessageId);
        }

        if (filter.MessageIds is { Count: > 0 })
        {
            query = query.Where(message => filter.MessageIds.Contains(message.Id));
        }

        var statusValues = MaterializeStatusFilter(filter.Statuses);

        if (statusValues is not null)
        {
            query = query.Where(message => statusValues.Contains((int) message.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.ContractName))
        {
            query = query.Where(message => message.ContractName == filter.ContractName);
        }

        if (!string.IsNullOrWhiteSpace(filter.Topic))
        {
            query = query.Where(message => message.Topic == filter.Topic);
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
    /// <param name="query">The outbox messages to page.</param>
    /// <param name="cursor">The opaque cursor from a previous page.</param>
    /// <returns>The query positioned after the supplied cursor.</returns>
    private static IQueryable<OutboxMessageEntity> ApplyCursor(
        IQueryable<OutboxMessageEntity> query,
        string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return query;
        }

        if (!OutboxMessagePageCursor.TryDecode(cursor, out var cursorCreatedAt, out var cursorMessageId))
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor));
        }

        return query.Where(message =>
            message.CreatedAt > cursorCreatedAt ||
            message.CreatedAt == cursorCreatedAt && message.Id > cursorMessageId);
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
    ///     Deletes rows matched by a filtered query, using bulk delete when the provider supports it.
    /// </summary>
    /// <typeparam name="TEntity">The outbox entity type.</typeparam>
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
    private static int[]? MaterializeStatusFilter(IReadOnlyList<OutboxStatus>? statuses)
    {
        if (statuses is not { Count: > 0 })
        {
            return null;
        }

        var values = new int[statuses.Count];

        for (var index = 0; index < statuses.Count; index++)
        {
            values[index] = (int) statuses[index];
        }

        return values;
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

    /// <summary>
    ///     Throws when a duplicate idempotency key or message identifier is rejected under strict conflict mode.
    /// </summary>
    /// <param name="envelope">The envelope attempted for insert.</param>
    /// <param name="existing">The conflicting entity already stored or tracked by the context.</param>
    private static void ThrowIfStrictConflict(OutboxEnvelope envelope, OutboxMessageEntity existing)
    {
        if (envelope.IdempotencyConflictMode != IdempotencyConflictMode.Strict || envelope.Id == existing.Id)
        {
            return;
        }

        throw new IdempotencyConflictException(
            $"An outbox message with idempotency key '{envelope.IdempotencyKey}' or message id '{envelope.Id}' already exists.");
    }
}