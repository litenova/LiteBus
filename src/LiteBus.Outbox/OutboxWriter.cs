using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default writer that turns an event instance into an outbox envelope.
/// </summary>
/// <remarks>
///     <para>
///         The writer performs only acceptance work: contract lookup, serialization, metadata mapping, and append to the
///         configured <see cref="IOutboxStore" />. It does not publish the event. Publication belongs to
///         <see cref="OutboxProcessor" /> and the configured <see cref="IOutboxDispatcher" />.
///     </para>
///     <para>
///         The runtime event type is used for contract lookup so closed generic event instances are stored with the
///         contract registered for that closed type. A stable message id can be supplied through <see cref="OutboxOptions" />.
///     </para>
/// </remarks>
public sealed class OutboxWriter : IOutbox
{
    /// <summary>
    ///     Gets the time provider used to stamp storage time.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime event type to a stable contract.
    /// </summary>
    private readonly IMessageContractRegistry _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the outbox writer store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IOutboxStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxWriter" /> class.
    /// </summary>
    /// <param name="store">The outbox writer store used to persist newly accepted envelopes.</param>
    /// <param name="contractRegistry">The registry used to map the runtime event type to a stable contract.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp storage time.</param>
    public OutboxWriter(
        IOutboxStore store,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt<TEvent>> AddAsync<TEvent>(
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
            TenantId = options.TenantId
        };

        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return new OutboxReceipt<TEvent>
        {
            Id = storedEnvelope.Id,
            MessageType = eventType,
            ContractName = storedEnvelope.ContractName,
            ContractVersion = storedEnvelope.ContractVersion,
            StoredAt = storedEnvelope.CreatedAt,
            CorrelationId = storedEnvelope.CorrelationId,
            CausationId = storedEnvelope.CausationId,
            TenantId = storedEnvelope.TenantId
        };
    }
}