using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Enqueues events through <see cref="LiteBusOutboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IOutbox" /> while rows commit with the active
///     <see cref="Microsoft.EntityFrameworkCore.DbContext" /> transaction.
/// </summary>
public sealed class TransactionalOutbox : ITransactionalOutbox
{
    /// <summary>
    ///     Gets the time provider used to stamp storage time on staged envelopes.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime event type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusOutboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalOutbox" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="contractRegistry">The registry used to map the runtime event type to a stable contract.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp storage time on staged envelopes.</param>
    public TransactionalOutbox(
        LiteBusOutboxSaveChangesInterceptor interceptor,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);

        options ??= new OutboxOptions();

        var eventType = @event.GetType();
        var contract = _contractRegistry.GetContract(eventType);
        var storedAt = _clock.GetUtcNow();
        var messageId = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(@event, cancellationToken).ConfigureAwait(false);

        var envelope = new OutboxEnvelope
        {
            Id = messageId,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            Topic = options.Topic,
            CreatedAt = storedAt,
            VisibleAfter = options.VisibleAfter,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            TenantId = options.TenantId,
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            TraceContext = options.TraceContext
        };

        _interceptor.Enqueue(envelope);

        return new OutboxReceipt<TEvent>
        {
            Id = envelope.Id,
            MessageType = eventType,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            StoredAt = envelope.CreatedAt,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId
        };
    }
}
