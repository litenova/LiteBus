using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Accepts messages through <see cref="LiteBusInboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IInbox" /> while rows commit with the active
///     <see cref="DbContext" /> transaction.
/// </summary>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
public sealed class TransactionalInbox<TContext> : ITransactionalInbox<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Gets the factory used to create envelopes before interceptor staging.
    /// </summary>
    private readonly IInboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusInboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalInbox{TContext}" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before interceptor staging.</param>
    /// <param name="clock">The time provider reserved for factory visibility resolution.</param>
    public TransactionalInbox(
        LiteBusInboxSaveChangesInterceptor interceptor,
        TContext dbContext,
        IInboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _ = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<InboxReceipt> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = await _envelopeFactory
            .CreateAsync(InboxAcceptItems.From(item), cancellationToken)
            .ConfigureAwait(false);

        _interceptor.Enqueue(_dbContext, envelope);

        return CreateReceipt(envelope, item.Message.GetType());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Array.Empty<InboxReceipt>();
        }

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var receipts = new InboxReceipt[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            _interceptor.Enqueue(_dbContext, envelopes[index]);
            receipts[index] = CreateReceipt(envelopes[index], items[index].Message.GetType());
        }

        return receipts;
    }

    /// <summary>
    ///     Maps a staged envelope to an acceptance receipt.
    /// </summary>
    /// <param name="envelope">The envelope staged for the next <c>SaveChanges</c> call.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The acceptance receipt returned to callers.</returns>
    private static InboxReceipt CreateReceipt(InboxEnvelope envelope, Type messageType)
    {
        return new InboxReceipt
        {
            Id = envelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = envelope.ContractName,
                Version = envelope.ContractVersion
            },
            AcceptedAt = envelope.CreatedAt,
            Trace = ResolveTrace(envelope.CorrelationId, envelope.CausationId, envelope.TraceContext),
            Tenant = ResolveTenant(envelope.TenantId)
        };
    }

    /// <summary>
    ///     Reconstructs trace metadata from persisted envelope columns.
    /// </summary>
    /// <param name="correlationId">The optional correlation identifier stored with the envelope.</param>
    /// <param name="causationId">The optional causation identifier stored with the envelope.</param>
    /// <param name="traceContext">The optional distributed trace context stored with the envelope.</param>
    /// <returns>The trace metadata represented by the stored columns.</returns>
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
    ///     Reconstructs tenant metadata from the persisted tenant identifier column.
    /// </summary>
    /// <param name="tenantId">The optional tenant identifier stored with the envelope.</param>
    /// <returns>The tenant metadata represented by the stored column.</returns>
    private static TenantScope ResolveTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? TenantScope.Unscoped.Instance
            : new TenantScope.Isolated(tenantId);
    }
}