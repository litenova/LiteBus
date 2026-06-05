using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Accepts messages through <see cref="LiteBusInboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IInbox" /> while rows commit with the active
///     <see cref="DbContext" /> transaction.
/// </summary>
public sealed class TransactionalInbox : ITransactionalInbox
{
    /// <summary>
    ///     Gets the time provider used to stamp storage time on staged envelopes.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime message type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly DbContext _dbContext;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusInboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalInbox" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="contractRegistry">The registry used to map the runtime message type to a stable contract.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp storage time on staged envelopes.</param>
    public TransactionalInbox(
        LiteBusInboxSaveChangesInterceptor interceptor,
        DbContext dbContext,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<InboxReceipt> AcceptAsync(
        object message,
        Type messageType,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);

        if (!messageType.IsInstanceOfType(message))
        {
            throw new ArgumentException(
                $"The supplied message instance is not assignable to '{messageType.FullName}'.",
                nameof(message));
        }

        options ??= new InboxOptions();

        var contract = _contractRegistry.GetContract(messageType);
        var acceptedAt = _clock.GetUtcNow();
        var messageId = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);

        var envelope = new InboxEnvelope
        {
            Id = messageId,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            CreatedAt = acceptedAt,
            VisibleAfter = options.VisibleAfter,
            Status = InboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            TenantId = options.TenantId,
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            TraceContext = options.TraceContext
        };

        _interceptor.Enqueue(_dbContext, envelope);

        return new InboxReceipt
        {
            Id = envelope.Id,
            MessageType = messageType,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            AcceptedAt = envelope.CreatedAt,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId
        };
    }

    /// <inheritdoc />
    public async Task<InboxReceipt<T>> AcceptAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var receipt = await AcceptAsync(message, message.GetType(), options, cancellationToken).ConfigureAwait(false);

        return new InboxReceipt<T>
        {
            Id = receipt.Id,
            MessageType = receipt.MessageType,
            ContractName = receipt.ContractName,
            ContractVersion = receipt.ContractVersion,
            AcceptedAt = receipt.AcceptedAt,
            CorrelationId = receipt.CorrelationId,
            CausationId = receipt.CausationId,
            TenantId = receipt.TenantId
        };
    }
}
