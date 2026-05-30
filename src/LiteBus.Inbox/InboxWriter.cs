using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default implementation of <see cref="IInbox" /> that serializes a message and writes it to the inbox store.
/// </summary>
/// <remarks>
///     <para>
///         Performs acceptance work only: contract lookup, serialization, metadata mapping, and write to the configured
///         <see cref="IInboxStore" />. Execution belongs to <see cref="InboxProcessor" /> and the configured
///         <see cref="IInboxDispatcher" /> registered separately from the core inbox module.
///     </para>
///     <para>
///         The runtime message type is used for contract lookup so closed generic instances are stored with the contract
///         registered for that closed type.
///     </para>
/// </remarks>
public sealed class InboxWriter : IInbox
{
    /// <summary>
    ///     Gets the time provider used to stamp acceptance time.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime message type to a stable contract.
    /// </summary>
    private readonly IMessageContractRegistry _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the inbox store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IInboxStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxWriter" /> class.
    /// </summary>
    /// <param name="store">The inbox store used to persist newly accepted envelopes.</param>
    /// <param name="contractRegistry">The registry used to map the runtime message type to a stable contract.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp acceptance time.</param>
    public InboxWriter(
        IInboxStore store,
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
    public async Task<InboxReceipt<T>> AddAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);

        options ??= new InboxOptions();

        var messageType = message.GetType();
        var contract = _contractRegistry.GetContract(messageType);
        var acceptedAt = _clock.GetUtcNow();
        var id = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);

        var envelope = new InboxEnvelope
        {
            Id = id,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            CreatedAt = acceptedAt,
            VisibleAfter = options.VisibleAfter,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            TenantId = options.TenantId
        };

        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return new InboxReceipt<T>
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            ContractName = storedEnvelope.ContractName,
            ContractVersion = storedEnvelope.ContractVersion,
            AcceptedAt = storedEnvelope.CreatedAt,
            CorrelationId = storedEnvelope.CorrelationId,
            CausationId = storedEnvelope.CausationId,
            TenantId = storedEnvelope.TenantId
        };
    }
}
