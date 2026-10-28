using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
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
    ///     Gets the enqueue options that enable ambient trace stamping and per-handler delivery.
    /// </summary>
    private readonly OutboxEnqueueOptions? _enqueueOptions;

    /// <summary>
    ///     Gets the optional registry used to resolve subscribed main handlers for per-handler delivery.
    /// </summary>
    private readonly IMessageReader? _messageReader;

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
    /// <param name="messageReader">
    ///     The optional registry used to resolve subscribed main handlers for per-handler delivery. Required when
    ///     <see cref="OutboxEnqueueOptions.PerHandlerDelivery" /> is enabled; composition supplies the shared registry.
    /// </param>
    /// <param name="enqueueOptions">The optional enqueue options. When omitted, ambient trace and per-handler delivery stay off.</param>
    public OutboxEnvelopeFactory(
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock,
        IOutboxPayloadProtector? payloadProtector = null,
        IMessageContractResolver? contractTypeResolver = null,
        IMessageReader? messageReader = null,
        OutboxEnqueueOptions? enqueueOptions = null)
    {
        ArgumentNullException.ThrowIfNull(contractRegistry);

        _contractRegistry = contractRegistry;
        ArgumentNullException.ThrowIfNull(messageSerializer);

        _messageSerializer = messageSerializer;
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _payloadProtector = payloadProtector;
        _contractTypeResolver = contractTypeResolver;
        _messageReader = messageReader;
        _enqueueOptions = enqueueOptions;
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
    public Task<IReadOnlyList<OutboxEnvelope>> CreateManyAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);

        return CreateManyCoreAsync(item.Message, item.Message.GetType(), item.Metadata, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEnvelope>> CreateManyAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return CreateManyCoreAsync(item.Message, item.MessageType, item.Metadata, cancellationToken);
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
            return [];
        }

        var tasks = new Task<IReadOnlyList<OutboxEnvelope>>[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            tasks[index] = CreateManyCoreAsync(
                item.Message,
                item.Message.GetType(),
                item.Metadata,
                cancellationToken);
        }

        var perItemEnvelopes = await Task.WhenAll(tasks).ConfigureAwait(false);
        List<OutboxEnvelope> envelopes = [];

        for (var index = 0; index < perItemEnvelopes.Length; index++)
        {
            envelopes.AddRange(perItemEnvelopes[index]);
        }

        return envelopes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return [];
        }

        var tasks = new Task<IReadOnlyList<OutboxEnvelope>>[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            tasks[index] = CreateManyCoreAsync(
                item.Message,
                item.MessageType,
                item.Metadata,
                cancellationToken);
        }

        var perItemEnvelopes = await Task.WhenAll(tasks).ConfigureAwait(false);
        List<OutboxEnvelope> envelopes = [];

        for (var index = 0; index < perItemEnvelopes.Length; index++)
        {
            envelopes.AddRange(perItemEnvelopes[index]);
        }

        return envelopes;
    }

    /// <summary>
    ///     Creates one outbox envelope from an event instance and enqueue metadata.
    /// </summary>
    /// <param name="eventInstance">The event instance to serialize.</param>
    /// <param name="eventType">The runtime event type used for contract lookup.</param>
    /// <param name="metadata">The enqueue metadata applied outside the payload.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The outbox envelope ready for store persistence or staging.</returns>
    /// <exception cref="ArgumentException">The distributed trace context is not valid JSON text.</exception>
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
        var tenantId = DurableEnvelopeMetadataMapper.ResolveTenantId(metadata.Tenant);
        var payload = await _messageSerializer.SerializeAsync(eventInstance, cancellationToken).ConfigureAwait(false);
        payload = await PayloadProtection.ProtectAsync(
                payload,
                _payloadProtector,
                new PayloadProtectionContext
                {
                    MessageId = id,
                    ContractName = contract.Name,
                    ContractVersion = contract.Version,
                    TenantId = tenantId,
                    Axis = "outbox"
                },
                cancellationToken)
            .ConfigureAwait(false);
        var trace = ResolveTrace(metadata.Trace);
        var (correlationId, causationId, traceContext) = DurableEnvelopeMetadataMapper.ResolveTraceColumns(trace);
        ThrowIfInvalidTraceContext(traceContext, metadata);

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
            TenantId = tenantId,
            IdempotencyKey = DurableEnvelopeMetadataMapper.ResolveIdempotencyKey(metadata.Idempotency),
            IdempotencyConflictMode = DurableEnvelopeMetadataMapper.ResolveIdempotencyConflictMode(metadata.Idempotency),
            TraceContext = traceContext
        };
    }

    /// <summary>
    ///     Creates one envelope per subscribed main handler when per-handler delivery is enabled, or a single broadcast envelope otherwise.
    /// </summary>
    /// <param name="eventInstance">The event instance to serialize.</param>
    /// <param name="eventType">The runtime event type used for contract lookup and handler resolution.</param>
    /// <param name="metadata">The enqueue metadata applied outside the payload.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The envelopes ready for store persistence or staging, ordered by handler full name.</returns>
    /// <exception cref="InvalidOperationException">Per-handler delivery is enabled but no message reader was supplied.</exception>
    /// <exception cref="NoHandlerFoundException">Per-handler delivery is enabled but no main handler subscribes to the event.</exception>
    private async Task<IReadOnlyList<OutboxEnvelope>> CreateManyCoreAsync(
        object eventInstance,
        Type eventType,
        OutboxEnqueueMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (_enqueueOptions?.PerHandlerDelivery != true)
        {
            return [await CreateCoreAsync(eventInstance, eventType, metadata, cancellationToken).ConfigureAwait(false)];
        }

        if (_messageReader is null)
        {
            throw new InvalidOperationException(
                "Per-handler delivery requires an IMessageReader. Compose through AddOutboxModule with " +
                "EnablePerHandlerDelivery, which supplies the shared registry, or pass the registry to the " +
                "OutboxEnvelopeFactory constructor.");
        }

        var handlers = ResolveHandlerIdentities(_messageReader, eventType);
        var envelopes = new OutboxEnvelope[handlers.Count];

        for (var index = 0; index < handlers.Count; index++)
        {
            var handler = handlers[index];
            var envelope = await CreateCoreAsync(
                    eventInstance,
                    eventType,
                    DerivePerHandlerMetadata(metadata, handler),
                    cancellationToken)
                .ConfigureAwait(false);

            envelopes[index] = envelope with { Handler = handler };
        }

        return envelopes;
    }

    /// <summary>
    ///     Resolves the distinct full names of the main handlers subscribed to an event, in ordinal order.
    /// </summary>
    /// <param name="messageReader">The registry used to resolve the event descriptor.</param>
    /// <param name="eventType">The runtime event type.</param>
    /// <returns>The subscribed handler identities.</returns>
    /// <exception cref="NoHandlerFoundException">No main handler subscribes to the event.</exception>
    /// <remarks>
    ///     Direct and indirect main handlers both count: a handler registered for a base event type still runs when the
    ///     event is published, so it still needs its own envelope. Descriptors are deduplicated by full name because one
    ///     handler class may implement several contracts for the same event.
    /// </remarks>
    private static IReadOnlyList<string> ResolveHandlerIdentities(IMessageReader messageReader, Type eventType)
    {
        var identities = new SortedSet<string>(StringComparer.Ordinal);
        var descriptor = messageReader.Find(eventType);

        if (descriptor is not null)
        {
            foreach (var handler in descriptor.Handlers)
            {
                identities.Add(handler.HandlerType.FullName ?? handler.HandlerType.Name);
            }

            foreach (var handler in descriptor.IndirectHandlers)
            {
                identities.Add(handler.HandlerType.FullName ?? handler.HandlerType.Name);
            }
        }

        if (identities.Count == 0)
        {
            throw new NoHandlerFoundException(eventType);
        }

        return [.. identities];
    }

    /// <summary>
    ///     Derives per-handler identity and idempotency metadata so each fanned-out envelope stays unique.
    /// </summary>
    /// <param name="metadata">The enqueue metadata supplied by the caller.</param>
    /// <param name="handler">The handler full name the envelope is addressed to.</param>
    /// <returns>Metadata with identity and idempotency derived for one handler.</returns>
    /// <remarks>
    ///     <para>
    ///         A caller-supplied message identifier is derived deterministically from the supplied value and the handler,
    ///         so a retried enqueue resolves to the same per-handler rows instead of inserting duplicates. Generated
    ///         identity needs no derivation because every envelope creation allocates a fresh identifier.
    ///     </para>
    ///     <para>
    ///         A keyed idempotency key is suffixed with the handler, following the same <c>{key}:{suffix}</c> shape as
    ///         <see cref="Idempotency.FromCommandKey" />, so per-handler envelopes deduplicate per handler rather than
    ///         collapsing onto one row. The conflict mode is preserved.
    ///     </para>
    /// </remarks>
    private static OutboxEnqueueMetadata DerivePerHandlerMetadata(OutboxEnqueueMetadata metadata, string handler)
    {
        MessageIdentity identity = metadata.Identity switch
        {
            MessageIdentity.Supplied supplied => new MessageIdentity.Supplied(DerivePerHandlerId(supplied.Value, handler)),
            _ => MessageIdentity.Generated.Instance
        };

        Idempotency idempotency = metadata.Idempotency switch
        {
            Idempotency.Keyed keyed => new Idempotency.Keyed($"{keyed.Key}:{handler}", keyed.ConflictMode),
            _ => Idempotency.None.Instance
        };

        return metadata with
        {
            Identity = identity,
            Idempotency = idempotency
        };
    }

    /// <summary>
    ///     Derives a deterministic message identifier for one per-handler envelope from the caller-supplied identifier.
    /// </summary>
    /// <param name="messageId">The message identifier supplied by the caller.</param>
    /// <param name="handler">The handler full name the envelope is addressed to.</param>
    /// <returns>A stable identifier unique to the supplied value and handler pair.</returns>
    private static Guid DerivePerHandlerId(Guid messageId, string handler)
    {
        var input = Encoding.UTF8.GetBytes($"{messageId:N}:{handler}");
        var hash = SHA256.HashData(input);

        return new Guid(new ReadOnlySpan<byte>(hash, 0, 16));
    }

    /// <summary>
    ///     Resolves the trace metadata stored with an envelope, stamping ambient trace when the item carries none and the option is on.
    /// </summary>
    /// <param name="trace">The trace metadata supplied by the caller.</param>
    /// <returns>The ambient trace when opted in and the item trace is none; otherwise the supplied trace.</returns>
    private MessageTrace ResolveTrace(MessageTrace trace)
    {
        if (trace is MessageTrace.None && _enqueueOptions?.UseAmbientTrace == true)
        {
            return MessageTrace.FromAmbient();
        }

        return trace;
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
            _ => null
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

    /// <summary>
    ///     Validates that a distributed trace context is valid JSON text before it reaches storage.
    /// </summary>
    /// <param name="traceContext">The trace context column value resolved from enqueue metadata.</param>
    /// <param name="metadata">The enqueue metadata that supplied the trace context.</param>
    /// <exception cref="ArgumentException">The trace context is not valid JSON text.</exception>
    /// <remarks>
    ///     PostgreSQL stores the value as jsonb and rejects non-JSON at insert, while other providers would persist it
    ///     and fail on read. Validating here fails fast on every provider with one clear error.
    /// </remarks>
    private static void ThrowIfInvalidTraceContext(string? traceContext, OutboxEnqueueMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(traceContext))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(traceContext);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The distributed trace context must be valid JSON text because the outbox stores it as JSON. " +
                "Pass a JSON object such as {\"traceparent\":\"00-...\"} instead of a raw traceparent string.",
                nameof(metadata),
                exception);
        }
    }
}
