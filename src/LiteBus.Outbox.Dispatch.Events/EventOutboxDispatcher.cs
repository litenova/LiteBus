using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Dispatch.Events;

/// <summary>
///     Dispatches outbox messages through the LiteBus in-process event publisher.
/// </summary>
/// <remarks>
///     <para>
///         This dispatcher is useful when the outbox should replay events into the local LiteBus event pipeline instead
///         of an external broker. It resolves the stored contract, deserializes the payload, then publishes the event
///         with the same event mediator semantics as an immediate <c>PublishAsync</c> call.
///     </para>
///     <para>
///         Events that implement <see cref="IEvent" /> are sent through the non-generic publisher overload. POCO events
///         are published through the generic overload by reflection because the event type is known only after contract
///         resolution.
///     </para>
/// </remarks>
public sealed class EventOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     Caches the open generic <c>PublishAsync&lt;TEvent&gt;</c> method used for POCO event publication.
    /// </summary>
    private static readonly MethodInfo GenericPublishMethod = typeof(IEventMediator)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(IEventMediator.PublishAsync) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 1);

    /// <summary>
    ///     Caches closed generic <c>PublishAsync&lt;TEvent&gt;</c> methods keyed by event type to avoid repeated reflection overhead.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedPublishMethodCache = new();

    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to event types.
    /// </summary>
    private readonly IMessageContractRegistry _contractRegistry;

    /// <summary>
    ///     Gets the LiteBus event publisher used as the dispatch target.
    /// </summary>
    private readonly IEventPublisher _eventPublisher;

    /// <summary>
    ///     Gets the serializer used to hydrate the persisted payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="eventPublisher">The LiteBus event publisher used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to event types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate the persisted payload.</param>
    public EventOutboxDispatcher(
        IEventPublisher eventPublisher,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var eventType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);
        var @event = await _messageSerializer.DeserializeAsync(eventType, message.Payload, cancellationToken).ConfigureAwait(false);
        var mediationSettings = CreateMediationSettings(message);

        if (@event is IEvent liteBusEvent)
        {
            await _eventPublisher.PublishAsync(liteBusEvent, mediationSettings, cancellationToken).ConfigureAwait(false);
            return;
        }

        var publishMethod = ClosedPublishMethodCache.GetOrAdd(eventType, t => GenericPublishMethod.MakeGenericMethod(t));

        Task publishTask;
        try
        {
            publishTask = publishMethod.Invoke(_eventPublisher, [@event, mediationSettings, cancellationToken]) as Task
                          ?? throw new InvalidOperationException(
                              $"The event publisher did not return a Task for '{eventType.FullName ?? eventType.Name}'.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is Exception inner)
        {
            publishTask = Task.FromException(inner);
        }

        await publishTask.ConfigureAwait(false);
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
            message.TenantId);

        return settings;
    }
}
