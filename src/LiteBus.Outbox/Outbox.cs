using System;

using System.Collections.Generic;

using System.Threading;

using System.Threading.Tasks;

using LiteBus.Messaging;
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

///         <see cref="PipelinedOutboxProcessor" /> and the configured <see cref="IOutboxDispatcher" />.

///     </para>

///     <para>

///         Contract lookup always uses <c>event.GetType()</c> so closed generic event instances are stored with the contract

///         registered for that closed type. A stable message id can be supplied through <see cref="OutboxOptions" />.

///     </para>

/// </remarks>

public sealed class Outbox : IOutbox, IOutboxScheduler

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

    ///     Gets the outbox writer store used to persist newly accepted envelopes.

    /// </summary>

    private readonly IOutboxStore _store;



    /// <summary>

    ///     Gets the optional outbox protector applied before payloads are written to storage.

    /// </summary>

    private readonly IOutboxPayloadProtector? _payloadProtector;



    /// <summary>
    ///     Gets the optional resolver that selects the CLR type used for contract lookup.
    /// </summary>
    private readonly IMessageContractResolver? _contractTypeResolver;



    /// <summary>

    ///     Initializes a new instance of the <see cref="Outbox" /> class.

    /// </summary>

    /// <param name="store">The outbox writer store used to persist newly accepted envelopes.</param>

    /// <param name="contractRegistry">The registry used to map event types to stable contracts.</param>

    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>

    /// <param name="clock">The time provider used to stamp storage time.</param>

    /// <param name="payloadProtector">The optional outbox protector applied before payloads are written to storage.</param>

    /// <param name="contractTypeResolver">
    ///     The optional resolver that overrides contract lookup type selection. When omitted, the runtime event type is
    ///     used.
    /// </param>

    public Outbox(

        IOutboxStore store,

        IContractReader contractRegistry,

        IMessageSerializer messageSerializer,

        TimeProvider clock,

        IOutboxPayloadProtector? payloadProtector = null,

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

    ///     Resolves the CLR type passed to <see cref="IContractReader.GetContract" /> for one enqueue call.

    /// </summary>

    /// <param name="declaredType">The declared event type supplied by the caller.</param>

    /// <param name="eventInstance">The event instance being enqueued.</param>

    /// <returns>The CLR type used for contract lookup.</returns>

    private Type ResolveContractType(Type declaredType, object eventInstance) =>

        _contractTypeResolver?.ResolveContractType(declaredType, eventInstance) ?? eventInstance.GetType();



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

        var contract = _contractRegistry.GetContract(ResolveContractType(eventType, @event));

        var storedAt = _clock.GetUtcNow();

        var messageId = options.Id ?? Guid.NewGuid();

        var payload = await _messageSerializer.SerializeAsync(@event, cancellationToken).ConfigureAwait(false);

        payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);



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



    /// <inheritdoc />

    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(

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

            return Array.Empty<OutboxReceipt>();

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



        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        var receipts = new OutboxReceipt[stored.Count];



        for (var index = 0; index < stored.Count; index++)

        {

            var storedEnvelope = stored[index];

            receipts[index] = new OutboxReceipt

            {

                Id = storedEnvelope.Id,

                MessageType = eventTypes[index],

                ContractName = storedEnvelope.ContractName,

                ContractVersion = storedEnvelope.ContractVersion,

                StoredAt = storedEnvelope.CreatedAt,

                CorrelationId = storedEnvelope.CorrelationId,

                CausationId = storedEnvelope.CausationId,

                TenantId = storedEnvelope.TenantId

            };

        }



        return receipts;

    }



    /// <inheritdoc />

    public async Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(

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

            return Array.Empty<OutboxReceipt<TEvent>>();

        }



        var envelopes = new OutboxEnvelope[events.Count];



        for (var index = 0; index < events.Count; index++)

        {

            var @event = events[index];

            var itemOptions = options?[index] ?? new OutboxOptions();

            var contract = _contractRegistry.GetContract(ResolveContractType(typeof(TEvent), @event));

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



        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        var receipts = new OutboxReceipt<TEvent>[stored.Count];



        for (var index = 0; index < stored.Count; index++)

        {

            var storedEnvelope = stored[index];

            receipts[index] = new OutboxReceipt<TEvent>

            {

                Id = storedEnvelope.Id,

                MessageType = events[index].GetType(),

                ContractName = storedEnvelope.ContractName,

                ContractVersion = storedEnvelope.ContractVersion,

                StoredAt = storedEnvelope.CreatedAt,

                CorrelationId = storedEnvelope.CorrelationId,

                CausationId = storedEnvelope.CausationId,

                TenantId = storedEnvelope.TenantId

            };

        }



        return receipts;

    }



    /// <inheritdoc />

    public Task<OutboxReceipt<TEvent>> ScheduleAsync<TEvent>(

        TEvent @event,

        DateTimeOffset enqueueAt,

        OutboxOptions? options = null,

        CancellationToken cancellationToken = default)

        where TEvent : notnull

    {

        return EnqueueAsync(@event, WithVisibleAfter(options, enqueueAt), cancellationToken);

    }



    /// <inheritdoc />

    public Task<OutboxReceipt<TEvent>> ScheduleAfterAsync<TEvent>(

        TEvent @event,

        TimeSpan delay,

        OutboxOptions? options = null,

        CancellationToken cancellationToken = default)

        where TEvent : notnull

    {

        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));



        return EnqueueAsync(@event, WithVisibleAfter(options, _clock.GetUtcNow().Add(delay)), cancellationToken);

    }



    /// <summary>

    ///     Merges the supplied options with a scheduled visibility timestamp.

    /// </summary>

    /// <param name="options">The caller-supplied outbox options, if any.</param>

    /// <param name="visibleAfter">The UTC timestamp when the event becomes visible to processors.</param>

    /// <returns>Outbox options with <see cref="OutboxOptions.VisibleAfter" /> set.</returns>

    private static OutboxOptions WithVisibleAfter(OutboxOptions? options, DateTimeOffset visibleAfter)

    {

        options ??= new OutboxOptions();



        return options with { VisibleAfter = visibleAfter };

    }

}

