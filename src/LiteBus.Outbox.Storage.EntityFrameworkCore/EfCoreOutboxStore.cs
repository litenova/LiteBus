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
///         The in-memory provider uses a process-wide lock with the same visibility rules so unit tests can run
///         without a database.
///     </para>
///     <para>
///         Applications own the <see cref="DbContext" /> and migrations. Call
///         <see cref="OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> from
///         <c>OnModelCreating</c> to align schema with this store.
///     </para>
/// </remarks>
public sealed class EfCoreOutboxStore : IOutboxStore, IOutboxLeaseStore, IOutboxStateStore
{
    /// <summary>
    ///     Synchronizes in-memory leasing when multiple workers run in one process.
    /// </summary>
    private readonly object _inMemoryLeaseLock = new();

    /// <summary>
    ///     Resolves a database context for direct factory construction used in tests.
    /// </summary>
    private readonly Func<CancellationToken, Task<IOutboxDbContext>>? _contextFactory;

    /// <summary>
    ///     The Entity Framework Core context type registered for scoped resolution.
    /// </summary>
    private readonly Type? _dbContextType;

    /// <summary>
    ///     Store options that define schema and table names for raw SQL leasing.
    /// </summary>
    private readonly EfCoreOutboxStoreOptions _options;

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

    /// <inheritdoc />
    public async Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return await ExecuteAsync(async (context, token) =>
        {
            var existing = await FindExistingEntityAsync(context, envelope.Id, token).ConfigureAwait(false);
            if (existing is not null)
            {
                return ToEnvelope(existing);
            }

            var entity = ToEntity(envelope);
            context.OutboxMessages.Add(entity);

            try
            {
                await SaveChangesAsync(context, token).ConfigureAwait(false);
                var stored = await FindExistingEntityAsync(context, envelope.Id, token).ConfigureAwait(false);
                return ToEnvelope(stored ?? entity);
            }
            catch (DbUpdateException)
            {
                var stored = await FindExistingEntityAsync(context, envelope.Id, token).ConfigureAwait(false);

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

            if (provider == EfCoreStorageProvider.InMemory)
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

            entity.Status = OutboxStatus.Published;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = null;
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

            entity.Status = OutboxStatus.Failed;
            entity.VisibleAfter = failure.VisibleAfter;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = failure.Error;
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

            entity.Status = OutboxStatus.DeadLettered;
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
        Func<IOutboxDbContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
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
        if (provider == EfCoreStorageProvider.Sqlite)
        {
            throw new NotSupportedException(
                "SQLite does not support concurrent outbox leasing for multiple processors. " +
                "Use PostgreSQL, SQL Server, MySQL, or run a single outbox processor instance.");
        }

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
    private Task<IReadOnlyList<OutboxEnvelope>> LeasePendingInMemoryAsync(
        IOutboxDbContext context,
        OutboxLeaseRequest request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        lock (_inMemoryLeaseLock)
        {
            return Task.FromResult(LeasePendingInMemoryCore(context, request));
        }
    }

    /// <summary>
    ///     Leases messages from the in-memory provider inside the in-memory lease lock.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="request">The lease request.</param>
    /// <returns>The leased message envelopes.</returns>
    private static IReadOnlyList<OutboxEnvelope> LeasePendingInMemoryCore(
        IOutboxDbContext context,
        OutboxLeaseRequest request)
    {
        var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
        var candidates = context.OutboxMessages
            .ToList()
            .Where(message => IsAvailable(message, request.Now))
            .OrderBy(message => message.CreatedAt)
            .Take(request.BatchSize)
            .ToList();

        foreach (var message in candidates)
        {
            message.Status = OutboxStatus.Publishing;
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
    ///     Determines whether a message is eligible for leasing at the supplied time.
    /// </summary>
    /// <param name="message">The persisted message.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns><see langword="true" /> when the message can be leased; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(OutboxMessageEntity message, DateTimeOffset now)
    {
        return (message.Status is OutboxStatus.Pending or OutboxStatus.Failed
                && (message.VisibleAfter is null || message.VisibleAfter <= now))
               || (message.Status == OutboxStatus.Publishing
                   && message.LeaseExpiresAt is not null
                   && message.LeaseExpiresAt <= now);
    }

    /// <summary>
    ///     Finds an existing row after a duplicate insert attempt.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="messageId">The message identifier from the attempted insert.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The existing entity when found; otherwise, <see langword="null" />.</returns>
    private static async Task<OutboxMessageEntity?> FindExistingEntityAsync(
        IOutboxDbContext context,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        return await context.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken)
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
            TenantId = entity.TenantId
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
            TenantId = row.TenantId
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
            TenantId = envelope.TenantId
        };
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
    }
}
