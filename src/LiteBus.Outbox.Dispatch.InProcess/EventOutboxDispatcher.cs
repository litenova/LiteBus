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
///     <para>
///         An envelope that records one handler publishes to that handler alone; an envelope without a recorded
///         handler publishes to every subscribed handler.
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
    ///     Gets the optional outbox protector used to decrypt stored payloads before deserialization.
    /// </summary>
    private readonly IOutboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the dispatch options that decide how a message with no handler is treated.
    /// </summary>
    private readonly InProcessOutboxDispatchOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="eventPublisher">The LiteBus event publisher used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to event types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate the persisted payload.</param>
    /// <param name="payloadProtector">The optional outbox protector used to decrypt stored payloads before deserialization.</param>
    /// <param name="options">The dispatch options, or null to require a handler for every dispatched message.</param>
    public EventOutboxDispatcher(
        IEventMediator eventPublisher,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        IOutboxPayloadProtector? payloadProtector = null,
        InProcessOutboxDispatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(eventPublisher);

        _eventPublisher = eventPublisher;
        ArgumentNullException.ThrowIfNull(contractRegistry);

        _contractRegistry = contractRegistry;
        ArgumentNullException.ThrowIfNull(messageSerializer);

        _messageSerializer = messageSerializer;
        _payloadProtector = payloadProtector;
        _options = options ?? new InProcessOutboxDispatchOptions();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The final <see cref="Exception" /> handler covers any serialization, contract, mediation, or handler failure
    ///     because the dispatch boundary must map every failure to <see cref="LiteBusDispatchException" /> for the
    ///     processor retry policy. Caller-requested cancellation is rethrown unwrapped.
    /// </remarks>
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var messageType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);

            var payload = await PayloadProtection.UnprotectAsync(
                    message.Payload,
                    _payloadProtector,
                    new PayloadProtectionContext
                    {
                        MessageId = message.Id,
                        ContractName = message.ContractName,
                        ContractVersion = message.ContractVersion,
                        TenantId = message.TenantId,
                        Axis = "outbox"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var messageInstance = await _messageSerializer.DeserializeAsync(messageType, payload, cancellationToken).ConfigureAwait(false);
            var mediationSettings = CreateMediationSettings(message, _options);

            if (messageInstance is IEvent liteBusEvent)
            {
                await _eventPublisher.PublishAsync(liteBusEvent, mediationSettings, cancellationToken).ConfigureAwait(false);
                return;
            }

            var publish = PublishDelegateCache.GetOrAdd(messageType, CreatePublishDelegate);
            await publish(_eventPublisher, messageInstance, mediationSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (LiteBusDispatchException)
        {
            throw;
        }

        // The caller's cancellation is a decision, not a dispatch failure. Wrapping it would hide it from the envelope
        // handler, which then charges a shutdown or a drain to the message's retry budget.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Dispatch boundary maps any serialization or handler failure to LiteBusDispatchException.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new LiteBusDispatchException(CreateFailureMessage(message, exception), exception);
        }
    }

    /// <summary>
    ///     Builds the operator-facing message for a failed dispatch.
    /// </summary>
    /// <param name="message">The outbox envelope that failed to dispatch.</param>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <returns>The failure message, with registration guidance only when the cause is a registration problem.</returns>
    /// <remarks>
    ///     Registration guidance on an unrelated failure, such as a concurrency conflict raised by a handler, sends the
    ///     reader after a configuration bug that does not exist, so it is added only for the failures it explains.
    /// </remarks>
    private static string CreateFailureMessage(OutboxEnvelope message, Exception exception)
    {
        var summary =
            $"Outbox dispatch failed for contract '{message.ContractName}' version {message.ContractVersion}: " +
            $"{exception.GetType().Name}: {exception.Message}";

        if (exception is not (MessageContractNotRegisteredException or NoHandlerFoundException))
        {
            return summary;
        }

        return summary +
               " Verify the event type is registered with Contracts.Register, implements IEvent or is a supported POCO " +
               "event, and that AddEventModule registered handlers for the event. When the event handlers live in an " +
               "assembly AddEvents does not scan, the message is dispatched to nothing; set RequireHandler to false on " +
               "UseInProcessDispatch only when publishing to zero handlers is intended.";
    }

    /// <summary>
    ///     Publishes a POCO event through the generic <see cref="IEventMediator.PublishAsync{TEvent}" /> overload.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type closed from the persisted contract.</typeparam>
    /// <param name="eventPublisher">The event mediator used as the dispatch target.</param>
    /// <param name="messageInstance">The deserialized message instance.</param>
    /// <param name="mediationSettings">The mediation settings copied from the outbox envelope.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>A task that completes when publication finishes.</returns>
    private static Task PublishTypedAsync<TEvent>(
        IEventMediator eventPublisher,
        object messageInstance,
        EventMediationSettings? mediationSettings,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        return eventPublisher.PublishAsync((TEvent) messageInstance, mediationSettings, cancellationToken);
    }

    /// <summary>
    ///     Creates a closed generic publish delegate for the supplied event type.
    /// </summary>
    /// <param name="messageType">The runtime message type resolved from the outbox contract.</param>
    /// <returns>A delegate that publishes through <see cref="IEventMediator.PublishAsync{TEvent}" />.</returns>
    /// <exception cref="LiteBusDispatchException">The publish delegate could not be created for the message type.</exception>
    /// <remarks>
    ///     The final <see cref="Exception" /> handler covers any reflection failure from <c>MakeGenericMethod</c> or
    ///     delegate creation because the dispatch boundary must report an unusable event type as
    ///     <see cref="LiteBusDispatchException" /> with registration guidance.
    /// </remarks>
    private static Func<IEventMediator, object, EventMediationSettings?, CancellationToken, Task> CreatePublishDelegate(Type messageType)
    {
        try
        {
            var closedMethod = PublishTypedAsyncMethod.MakeGenericMethod(messageType);

            return (mediator, messageInstance, settings, cancellationToken) =>
                (Task) closedMethod.Invoke(null, [mediator, messageInstance, settings, cancellationToken])!;
        }
#pragma warning disable CA1031 // Reflection over any event type can fail; the dispatch boundary reports it as LiteBusDispatchException.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new LiteBusDispatchException(
                $"Outbox dispatch could not create a publish delegate for message type '{messageType.FullName}'. " +
                "Register the closed event type with Contracts.Register and ensure it is a valid POCO or IEvent implementation.",
                exception);
        }
    }

    /// <summary>
    ///     Creates event mediation settings with trace metadata copied from the outbox envelope.
    /// </summary>
    /// <param name="message">The outbox message whose correlation, causation, and tenant values should be applied.</param>
    /// <param name="options">The dispatch options that decide how a message with no handler is treated.</param>
    /// <returns>Event mediation settings configured for outbox replay.</returns>
    /// <remarks>
    ///     An envelope addressed to one handler by per-handler delivery publishes with a handler predicate matching
    ///     only that main handler, so the reaction retries alone while pipeline observers still run. When the recorded
    ///     handler no longer subscribes, the predicate matches no main handler and
    ///     <see cref="InProcessOutboxDispatchOptions.RequireHandler" /> decides whether the attempt fails or completes
    ///     as published.
    /// </remarks>
    private static EventMediationSettings CreateMediationSettings(
        OutboxEnvelope message,
        InProcessOutboxDispatchOptions options)
    {
        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = options.RequireHandler,
            Routing = CreateRoutingSettings(message)
        };

        MessageProcessorDiagnostics.ApplyTraceMetadata(
            settings.Items,
            message.CorrelationId,
            message.CausationId,
            message.TenantId,
            message.TraceContext);

        return settings;
    }

    /// <summary>
    ///     Creates routing settings that restrict publication to the recorded handler for a per-handler envelope.
    /// </summary>
    /// <param name="message">The outbox message being dispatched.</param>
    /// <returns>Routing settings matching only the recorded handler, or default routing for a broadcast envelope.</returns>
    /// <remarks>
    ///     The predicate restricts main handlers only. Pre-stage, post, error, and completion observers observe every
    ///     mediation by message type rather than by main handler, so they pass through and run on each targeted
    ///     dispatch exactly as they would on a broadcast.
    /// </remarks>
    private static EventRoutingSettings CreateRoutingSettings(OutboxEnvelope message)
    {
        if (message.Handler is null)
        {
            return new EventRoutingSettings();
        }

        var handler = message.Handler;

        return new EventRoutingSettings
        {
            HandlerPredicate = descriptor => descriptor is not IMainHandlerDescriptor ||
                string.Equals(
                    descriptor.HandlerType.FullName,
                    handler,
                    StringComparison.Ordinal)
        };
    }
}
