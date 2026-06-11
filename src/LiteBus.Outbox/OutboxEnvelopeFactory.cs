using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default implementation of <see cref="IOutboxEnvelopeFactory" />.
/// </summary>
public sealed class OutboxEnvelopeFactory : IOutboxEnvelopeFactory
{
    /// <summary>
    ///     Gets the time provider used to stamp storage time.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime event type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional outbox protector applied before payloads are written to storage.
    /// </summary>
    private readonly IOutboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the optional resolver that selects the CLR type used for contract lookup.
    /// </summary>
    private readonly IMessageContractResolver? _contractTypeResolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxEnvelopeFactory" /> class.
    /// </summary>
    /// <param name="contractRegistry">The registry used to map event types to stable contracts.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp storage time.</param>
    /// <param name="payloadProtector">The optional outbox protector applied before payloads are written to storage.</param>
    /// <param name="contractTypeResolver">
    ///     The optional resolver that overrides contract lookup type selection. When omitted, the runtime event type is
    ///     used.
    /// </param>
    public OutboxEnvelopeFactory(
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock,
        IOutboxPayloadProtector? payloadProtector = null,
        IMessageContractResolver? contractTypeResolver = null)
    {
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _payloadProtector = payloadProtector;
        _contractTypeResolver = contractTypeResolver;
    }

    /// <inheritdoc />
    public async Task<OutboxEnvelope> CreateAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);

        options ??= new OutboxOptions();

        var eventType = @event.GetType();
        var contract = _contractRegistry.GetContract(ResolveContractType(eventType, @event));
        var storedAt = _clock.GetUtcNow();
        var messageId = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(@event, cancellationToken).ConfigureAwait(false);
        payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);

        return new OutboxEnvelope
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
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(eventTypes);

        if (events.Count != eventTypes.Count)
        {
            throw new ArgumentException("Events and event types must contain the same number of entries.");
        }

        if (options is not null && options.Count != events.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as events when supplied.");
        }

        if (events.Count == 0)
        {
            return Array.Empty<OutboxEnvelope>();
        }

        var envelopes = new OutboxEnvelope[events.Count];

        for (var index = 0; index < events.Count; index++)
        {
            var @event = events[index];
            var eventType = eventTypes[index];
            var itemOptions = options?[index] ?? new OutboxOptions();

            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(eventType);

            if (!eventType.IsInstanceOfType(@event))
            {
                throw new ArgumentException(
                    $"Event at index {index} is not assignable to '{eventType.FullName}'.",
                    nameof(events));
            }

            var contract = _contractRegistry.GetContract(ResolveContractType(eventType, @event));
            var storedAt = _clock.GetUtcNow();
            var messageId = itemOptions.Id ?? Guid.NewGuid();
            var payload = await _messageSerializer.SerializeAsync(@event, cancellationToken).ConfigureAwait(false);
            payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);

            envelopes[index] = new OutboxEnvelope
            {
                Id = messageId,
                ContractName = contract.Name,
                ContractVersion = contract.Version,
                Payload = payload,
                Topic = itemOptions.Topic,
                CreatedAt = storedAt,
                VisibleAfter = itemOptions.VisibleAfter,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                CorrelationId = itemOptions.CorrelationId,
                CausationId = itemOptions.CausationId,
                TenantId = itemOptions.TenantId,
                IdempotencyKey = string.IsNullOrWhiteSpace(itemOptions.IdempotencyKey) ? null : itemOptions.IdempotencyKey,
                TraceContext = itemOptions.TraceContext
            };
        }

        return envelopes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(events);

        if (options is not null && options.Count != events.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as events when supplied.");
        }

        if (events.Count == 0)
        {
            return Array.Empty<OutboxEnvelope>();
        }

        var envelopes = new OutboxEnvelope[events.Count];

        for (var index = 0; index < events.Count; index++)
        {
            envelopes[index] = await CreateAsync(events[index], options?[index], cancellationToken).ConfigureAwait(false);
        }

        return envelopes;
    }

    /// <summary>
    ///     Resolves the CLR type passed to <see cref="IContractReader.GetContract" /> for one enqueue call.
    /// </summary>
    /// <param name="declaredType">The declared event type supplied by the caller.</param>
    /// <param name="eventInstance">The event instance being enqueued.</param>
    /// <returns>The CLR type used for contract lookup.</returns>
    private Type ResolveContractType(Type declaredType, object eventInstance) =>
        _contractTypeResolver?.ResolveContractType(declaredType, eventInstance) ?? eventInstance.GetType();
}
