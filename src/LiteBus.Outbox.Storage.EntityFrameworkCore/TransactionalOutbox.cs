using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Enqueues events through <see cref="LiteBusOutboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IOutbox" /> while rows commit with the active
///     <see cref="DbContext" /> transaction.
/// </summary>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
public sealed class TransactionalOutbox<TContext> : ITransactionalOutbox<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Gets the factory used to create envelopes before interceptor staging.
    /// </summary>
    private readonly IOutboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusOutboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalOutbox{TContext}" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before interceptor staging.</param>
    /// <param name="clock">The time provider reserved for factory visibility resolution.</param>
    public TransactionalOutbox(
        LiteBusOutboxSaveChangesInterceptor interceptor,
        TContext dbContext,
        IOutboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _ = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        _interceptor.Enqueue(_dbContext, envelope);

        return CreateTypedReceipt<TEvent>(envelope, item.Event.GetType());
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        _interceptor.Enqueue(_dbContext, envelope);

        return CreateReceipt(envelope, item.EventType);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Array.Empty<OutboxReceipt<TEvent>>();
        }

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt<TEvent>[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            _interceptor.Enqueue(_dbContext, envelopes[index]);
            receipts[index] = CreateTypedReceipt<TEvent>(envelopes[index], items[index].Event.GetType());
        }

        return receipts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Array.Empty<OutboxReceipt>();
        }

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            _interceptor.Enqueue(_dbContext, envelopes[index]);
            receipts[index] = CreateReceipt(envelopes[index], items[index].EventType);
        }

        return receipts;
    }

    /// <summary>
    ///     Maps a staged envelope to an untyped enqueue receipt.
    /// </summary>
    /// <param name="envelope">The envelope staged for the next <c>SaveChanges</c> call.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    private static OutboxReceipt CreateReceipt(OutboxEnvelope envelope, Type messageType)
    {
        return new OutboxReceipt
        {
            Id = envelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = envelope.ContractName,
                Version = envelope.ContractVersion
            },
            StoredAt = envelope.CreatedAt,
            Trace = ResolveTrace(envelope.CorrelationId, envelope.CausationId, envelope.TraceContext),
            Tenant = ResolveTenant(envelope.TenantId)
        };
    }

    /// <summary>
    ///     Maps a staged envelope to a typed enqueue receipt.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the receipt.</typeparam>
    /// <param name="envelope">The envelope staged for the next <c>SaveChanges</c> call.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    private static OutboxReceipt<TEvent> CreateTypedReceipt<TEvent>(OutboxEnvelope envelope, Type messageType)
        where TEvent : notnull
    {
        var receipt = CreateReceipt(envelope, messageType);

        return new OutboxReceipt<TEvent>
        {
            Id = receipt.Id,
            MessageType = receipt.MessageType,
            Contract = receipt.Contract,
            StoredAt = receipt.StoredAt,
            Trace = receipt.Trace,
            Tenant = receipt.Tenant
        };
    }

    /// <summary>
    ///     Reconstructs trace metadata from staged envelope columns.
    /// </summary>
    /// <param name="correlationId">The optional correlation identifier stored with the envelope.</param>
    /// <param name="causationId">The optional causation identifier stored with the envelope.</param>
    /// <param name="traceContext">The optional distributed trace context stored with the envelope.</param>
    /// <returns>The trace metadata represented by the staged columns.</returns>
    private static MessageTrace ResolveTrace(
        string? correlationId,
        string? causationId,
        string? traceContext)
    {
        if (!string.IsNullOrWhiteSpace(traceContext) && !string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Distributed(correlationId, causationId, traceContext);
        }

        if (!string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Workflow(correlationId, causationId);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return new MessageTrace.Correlated(correlationId);
        }

        return MessageTrace.None.Instance;
    }

    /// <summary>
    ///     Reconstructs tenant metadata from the staged tenant identifier column.
    /// </summary>
    /// <param name="tenantId">The optional tenant identifier stored with the envelope.</param>
    /// <returns>The tenant metadata represented by the staged column.</returns>
    private static TenantScope ResolveTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? TenantScope.Unscoped.Instance
            : new TenantScope.Isolated(tenantId);
    }
}