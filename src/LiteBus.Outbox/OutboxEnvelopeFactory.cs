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
    ///     Gets the time provider used to stamp storage time and resolve relative visibility.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime event type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the optional resolver that selects the CLR type used for contract lookup.
    /// </summary>
    private readonly IMessageContractResolver? _contractTypeResolver;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional outbox protector applied before payloads are written to storage.
    /// </summary>
    private readonly IOutboxPayloadProtector? _payloadProtector;

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
    public Task<OutboxEnvelope> CreateAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);

        return CreateCoreAsync(item.Message, item.Message.GetType(), item.Metadata, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxEnvelope> CreateAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return CreateCoreAsync(item.Message, item.MessageType, item.Metadata, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Array.Empty<OutboxEnvelope>();
        }

        var tasks = new Task<OutboxEnvelope>[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            tasks[index] = CreateCoreAsync(
                item.Message,
                item.Message.GetType(),
                item.Metadata,
                cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Array.Empty<OutboxEnvelope>();
        }

        var tasks = new Task<OutboxEnvelope>[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            tasks[index] = CreateCoreAsync(
                item.Message,
                item.MessageType,
                item.Metadata,
                cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates one outbox envelope from an event instance and enqueue metadata.
    /// </summary>
    /// <param name="eventInstance">The event instance to serialize.</param>
    /// <param name="eventType">The runtime event type used for contract lookup.</param>
    /// <param name="metadata">The enqueue metadata applied outside the payload.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The outbox envelope ready for store persistence or staging.</returns>
    private async Task<OutboxEnvelope> CreateCoreAsync(
        object eventInstance,
        Type eventType,
        OutboxEnqueueMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventInstance);
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!eventType.IsInstanceOfType(eventInstance))
        {
            throw new ArgumentException(
                $"The supplied event instance is not assignable to '{eventType.FullName}'.",
                nameof(eventInstance));
        }

        var contract = _contractRegistry.GetContract(ResolveContractType(eventType, eventInstance));
        var storedAt = _clock.GetUtcNow();
        var id = DurableEnvelopeMetadataMapper.ResolveMessageId(metadata.Identity);
        var payload = await _messageSerializer.SerializeAsync(eventInstance, cancellationToken).ConfigureAwait(false);
        payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);
        var (correlationId, causationId, traceContext) = DurableEnvelopeMetadataMapper.ResolveTraceColumns(metadata.Trace);

        return new OutboxEnvelope
        {
            Id = id,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            Topic = ResolveTopic(metadata.Target),
            CreatedAt = storedAt,
            VisibleAfter = DurableEnvelopeMetadataMapper.ResolveVisibleAfter(metadata.Visibility, _clock),
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = DurableEnvelopeMetadataMapper.ResolveTenantId(metadata.Tenant),
            IdempotencyKey = DurableEnvelopeMetadataMapper.ResolveIdempotencyKey(metadata.Idempotency),
            IdempotencyConflictMode = DurableEnvelopeMetadataMapper.ResolveIdempotencyConflictMode(metadata.Idempotency),
            TraceContext = traceContext
        };
    }

    /// <summary>
    ///     Resolves the publication topic column value from publication target metadata.
    /// </summary>
    /// <param name="target">The publication target metadata supplied by the caller.</param>
    /// <returns>The topic to persist, or <see langword="null" /> when dispatchers should use contract defaults.</returns>
    private static string? ResolveTopic(PublicationTarget target)
    {
        return target switch
        {
            PublicationTarget.Topic topic when !string.IsNullOrWhiteSpace(topic.Name) => topic.Name,
            PublicationTarget.Exchange exchange when !string.IsNullOrWhiteSpace(exchange.Name) => exchange.Name,
            PublicationTarget.Queue queue when !string.IsNullOrWhiteSpace(queue.Name) => queue.Name,
            _                                                                         => null
        };
    }

    /// <summary>
    ///     Resolves the CLR type passed to <see cref="IContractReader.GetContract" /> for one enqueue call.
    /// </summary>
    /// <param name="declaredType">The declared event type supplied by the caller.</param>
    /// <param name="eventInstance">The event instance being enqueued.</param>
    /// <returns>The CLR type used for contract lookup.</returns>
    private Type ResolveContractType(Type declaredType, object eventInstance)
    {
        return _contractTypeResolver?.ResolveContractType(declaredType, eventInstance) ?? eventInstance.GetType();
    }
}