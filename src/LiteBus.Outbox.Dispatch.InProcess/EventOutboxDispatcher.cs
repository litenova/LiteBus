using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Dispatches outbox messages through the LiteBus in-process event mediator.
/// </summary>
/// <remarks>
///     <para>
///         This dispatcher is useful when the outbox should replay events into the local LiteBus event pipeline instead
///         of an external broker. It resolves the stored contract, deserializes the payload, then publishes the event
///         with the same event mediator semantics as an immediate <c>PublishAsync</c> call.
///     </para>
///     <para>
///         Events that implement <see cref="IEvent" /> are sent through the non-generic publisher overload. POCO events
///         are published through a closed generic helper cached per event type.
///     </para>
/// </remarks>
public sealed class EventOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     Caches closed generic publish delegates keyed by event type to avoid repeated reflection overhead.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<IEventMediator, object, EventMediationSettings?, CancellationToken, Task>> PublishDelegateCache = new();

    /// <summary>
    ///     The open generic publish helper resolved once at type initialization.
    /// </summary>
    private static readonly MethodInfo PublishTypedAsyncMethod = typeof(EventOutboxDispatcher)
                                                                     .GetMethod(nameof(PublishTypedAsync), BindingFlags.NonPublic | BindingFlags.Static) ??
                                                                 throw new InvalidOperationException($"Could not resolve {nameof(PublishTypedAsync)}.");

    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to event types.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the LiteBus event publisher used as the dispatch target.
    /// </summary>
    private readonly IEventMediator _eventPublisher;

    /// <summary>
    ///     Gets the serializer used to hydrate the persisted payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional encryptor used to decrypt stored payloads before deserialization.
    /// </summary>
    private readonly IPayloadEncryptor? _payloadEncryptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="eventPublisher">The LiteBus event publisher used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to event types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate the persisted payload.</param>
    /// <param name="payloadEncryptor">The optional encryptor used to decrypt stored payloads before deserialization.</param>
    public EventOutboxDispatcher(
        IEventMediator eventPublisher,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        IPayloadEncryptor? payloadEncryptor = null)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _payloadEncryptor = payloadEncryptor;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var eventType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);

            var payload = await PayloadProtection.UnprotectAsync(message.Payload, _payloadEncryptor, cancellationToken)
                .ConfigureAwait(false);

            var @event = await _messageSerializer.DeserializeAsync(eventType, payload, cancellationToken).ConfigureAwait(false);
            var mediationSettings = CreateMediationSettings(message);

            if (@event is IEvent liteBusEvent)
            {
                await _eventPublisher.PublishAsync(liteBusEvent, mediationSettings, cancellationToken).ConfigureAwait(false);
                return;
            }

            var publish = PublishDelegateCache.GetOrAdd(eventType, CreatePublishDelegate);
            await publish(_eventPublisher, @event, mediationSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (LiteBusDispatchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LiteBusDispatchException(
                $"Outbox dispatch failed for contract '{message.ContractName}' version {message.ContractVersion}. " +
                "Verify the event type is registered with Contracts.Register, implements IEvent or is a supported POCO event, " +
                "and that AddEventModule registered handlers for the event.",
                exception);
        }
    }

    /// <summary>
    ///     Publishes a POCO event through the generic <see cref="IEventMediator.PublishAsync{TEvent}" /> overload.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type closed from the persisted contract.</typeparam>
    /// <param name="eventPublisher">The event mediator used as the dispatch target.</param>
    /// <param name="eventInstance">The deserialized event instance.</param>
    /// <param name="mediationSettings">The mediation settings copied from the outbox envelope.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>A task that completes when publication finishes.</returns>
    private static Task PublishTypedAsync<TEvent>(
        IEventMediator eventPublisher,
        object eventInstance,
        EventMediationSettings? mediationSettings,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        return eventPublisher.PublishAsync((TEvent) eventInstance, mediationSettings, cancellationToken);
    }

    /// <summary>
    ///     Creates a closed generic publish delegate for the supplied event type.
    /// </summary>
    /// <param name="eventType">The runtime event type resolved from the outbox contract.</param>
    /// <returns>A delegate that publishes through <see cref="IEventMediator.PublishAsync{TEvent}" />.</returns>
    private static Func<IEventMediator, object, EventMediationSettings?, CancellationToken, Task> CreatePublishDelegate(Type eventType)
    {
        try
        {
            var closedMethod = PublishTypedAsyncMethod.MakeGenericMethod(eventType);

            return (mediator, eventInstance, settings, cancellationToken) =>
                (Task) closedMethod.Invoke(null, [mediator, eventInstance, settings, cancellationToken])!;
        }
        catch (Exception exception)
        {
            throw new LiteBusDispatchException(
                $"Outbox dispatch could not create a publish delegate for event type '{eventType.FullName}'. " +
                "Register the closed event type with Contracts.Register and ensure it is a valid POCO or IEvent implementation.",
                exception);
        }
    }

    /// <summary>
    ///     Creates event mediation settings with trace metadata copied from the outbox envelope.
    /// </summary>
    /// <param name="message">The outbox message whose correlation, causation, and tenant values should be applied.</param>
    /// <returns>Event mediation settings configured for outbox replay.</returns>
    private static EventMediationSettings CreateMediationSettings(OutboxEnvelope message)
    {
        var settings = new EventMediationSettings();

        MessageProcessorDiagnostics.ApplyTraceMetadata(
            settings.Items,
            message.CorrelationId,
            message.CausationId,
            message.TenantId,
            message.TraceContext);

        return settings;
    }
}