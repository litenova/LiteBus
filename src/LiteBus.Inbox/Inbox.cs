using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default implementation of <see cref="IInbox" /> that serializes a message and writes it to the inbox store.
/// </summary>
/// <remarks>
///     <para>
///         Performs acceptance work only: contract lookup, serialization, metadata mapping, and write to the configured
///         <see cref="IInboxStore" />. Execution belongs to <see cref="PipelinedInboxProcessor" /> and the configured
///         <see cref="IInboxDispatcher" /> registered separately from the core inbox module.
///     </para>
///     <para>
///         The runtime message type is used for contract lookup so closed generic instances are stored with the contract
///         registered for that closed type.
///     </para>
/// </remarks>
public sealed class Inbox : IInbox, IInboxScheduler
{
    /// <summary>
    ///     Gets the time provider used to stamp acceptance time.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime message type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the inbox store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IInboxStore _store;

    /// <summary>
    ///     Gets the optional inbox protector applied before payloads are written to storage.
    /// </summary>
    private readonly IInboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the optional resolver that selects the CLR type used for contract lookup.
    /// </summary>
    private readonly IMessageContractResolver? _contractTypeResolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Inbox" /> class.
    /// </summary>
    /// <param name="store">The inbox store used to persist newly accepted envelopes.</param>
    /// <param name="contractRegistry">The registry used to map message types to stable contracts.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp acceptance time.</param>
    /// <param name="payloadProtector">The optional inbox protector applied before payloads are written to storage.</param>
    /// <param name="contractTypeResolver">
    ///     The optional resolver that overrides contract lookup type selection. When omitted, the runtime message type is
    ///     used.
    /// </param>
    public Inbox(
        IInboxStore store,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock,
        IInboxPayloadProtector? payloadProtector = null,
        IMessageContractResolver? contractTypeResolver = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _payloadProtector = payloadProtector;
        _contractTypeResolver = contractTypeResolver;
    }

    /// <summary>
    ///     Resolves the CLR type passed to <see cref="IContractReader.GetContract" /> for one acceptance call.
    /// </summary>
    /// <param name="declaredType">The declared message type supplied by the caller.</param>
    /// <param name="message">The message instance being accepted.</param>
    /// <returns>The CLR type used for contract lookup.</returns>
    private Type ResolveContractType(Type declaredType, object message) =>
        _contractTypeResolver?.ResolveContractType(declaredType, message) ?? message.GetType();

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

        var contract = _contractRegistry.GetContract(ResolveContractType(messageType, message));
        var acceptedAt = _clock.GetUtcNow();
        var id = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
        payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);

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
            TenantId = options.TenantId,
            TraceContext = options.TraceContext
        };

        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return new InboxReceipt
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<Type> messageTypes,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(messageTypes);

        if (messages.Count != messageTypes.Count)
        {
            throw new ArgumentException("Messages and message types must contain the same number of entries.");
        }

        if (options is not null && options.Count != messages.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as messages when supplied.");
        }

        if (messages.Count == 0)
        {
            return Array.Empty<InboxReceipt>();
        }

        var envelopes = new InboxEnvelope[messages.Count];

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var messageType = messageTypes[index];
            var itemOptions = options?[index] ?? new InboxOptions();

            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(messageType);

            if (!messageType.IsInstanceOfType(message))
            {
                throw new ArgumentException(
                    $"Message at index {index} is not assignable to '{messageType.FullName}'.",
                    nameof(messages));
            }

            var contract = _contractRegistry.GetContract(ResolveContractType(messageType, message));
            var acceptedAt = _clock.GetUtcNow();
            var id = itemOptions.Id ?? Guid.NewGuid();
            var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
            payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);

            envelopes[index] = new InboxEnvelope
            {
                Id = id,
                ContractName = contract.Name,
                ContractVersion = contract.Version,
                Payload = payload,
                CreatedAt = acceptedAt,
                VisibleAfter = itemOptions.VisibleAfter,
                AttemptCount = 0,
                Status = InboxStatus.Pending,
                IdempotencyKey = string.IsNullOrWhiteSpace(itemOptions.IdempotencyKey) ? null : itemOptions.IdempotencyKey,
                CorrelationId = itemOptions.CorrelationId,
                CausationId = itemOptions.CausationId,
                TenantId = itemOptions.TenantId,
                TraceContext = itemOptions.TraceContext
            };
        }

        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new InboxReceipt[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            var storedEnvelope = stored[index];
            receipts[index] = new InboxReceipt
            {
                Id = storedEnvelope.Id,
                MessageType = messageTypes[index],
                ContractName = storedEnvelope.ContractName,
                ContractVersion = storedEnvelope.ContractVersion,
                AcceptedAt = storedEnvelope.CreatedAt,
                CorrelationId = storedEnvelope.CorrelationId,
                CausationId = storedEnvelope.CausationId,
                TenantId = storedEnvelope.TenantId
            };
        }

        return receipts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReceipt<T>>> AcceptBatchAsync<T>(
        IReadOnlyList<T> messages,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (options is not null && options.Count != messages.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as messages when supplied.");
        }

        if (messages.Count == 0)
        {
            return Array.Empty<InboxReceipt<T>>();
        }

        var envelopes = new InboxEnvelope[messages.Count];

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var itemOptions = options?[index] ?? new InboxOptions();
            var contract = _contractRegistry.GetContract(ResolveContractType(typeof(T), message));
            var acceptedAt = _clock.GetUtcNow();
            var id = itemOptions.Id ?? Guid.NewGuid();
            var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
            payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);

            envelopes[index] = new InboxEnvelope
            {
                Id = id,
                ContractName = contract.Name,
                ContractVersion = contract.Version,
                Payload = payload,
                CreatedAt = acceptedAt,
                VisibleAfter = itemOptions.VisibleAfter,
                AttemptCount = 0,
                Status = InboxStatus.Pending,
                IdempotencyKey = string.IsNullOrWhiteSpace(itemOptions.IdempotencyKey) ? null : itemOptions.IdempotencyKey,
                CorrelationId = itemOptions.CorrelationId,
                CausationId = itemOptions.CausationId,
                TenantId = itemOptions.TenantId,
                TraceContext = itemOptions.TraceContext
            };
        }

        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new InboxReceipt<T>[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            var storedEnvelope = stored[index];
            receipts[index] = new InboxReceipt<T>
            {
                Id = storedEnvelope.Id,
                MessageType = messages[index].GetType(),
                ContractName = storedEnvelope.ContractName,
                ContractVersion = storedEnvelope.ContractVersion,
                AcceptedAt = storedEnvelope.CreatedAt,
                CorrelationId = storedEnvelope.CorrelationId,
                CausationId = storedEnvelope.CausationId,
                TenantId = storedEnvelope.TenantId
            };
        }

        return receipts;
    }

    /// <inheritdoc />
    public Task<InboxReceipt<T>> ScheduleAsync<T>(
        T message,
        DateTimeOffset enqueueAt,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        return AcceptAsync(message, WithVisibleAfter(options, enqueueAt), cancellationToken);
    }

    /// <inheritdoc />
    public Task<InboxReceipt<T>> ScheduleAfterAsync<T>(
        T message,
        TimeSpan delay,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));

        return AcceptAsync(message, WithVisibleAfter(options, _clock.GetUtcNow().Add(delay)), cancellationToken);
    }

    /// <summary>
    ///     Merges the supplied options with a scheduled visibility timestamp.
    /// </summary>
    /// <param name="options">The caller-supplied inbox options, if any.</param>
    /// <param name="visibleAfter">The UTC timestamp when the message becomes visible to processors.</param>
    /// <returns>Inbox options with <see cref="InboxOptions.VisibleAfter" /> set.</returns>
    private static InboxOptions WithVisibleAfter(InboxOptions? options, DateTimeOffset visibleAfter)
    {
        options ??= new InboxOptions();

        return options with { VisibleAfter = visibleAfter };
    }
}
