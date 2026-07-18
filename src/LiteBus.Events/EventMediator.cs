using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Events.MediationStrategies;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events;

/// <summary>
///     The primary implementation of <see cref="IEventMediator" />. It orchestrates the event publication
///     pipeline for immediate, in-process event broadcasting.
/// </summary>
public sealed class EventMediator : IEventMediator
{
    /// <summary>
    ///     Gets the core message mediator used to execute the event pipeline.
    /// </summary>
    private readonly IMessageMediator _messageMediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventMediator" /> class.
    /// </summary>
    /// <param name="messageMediator">The core message mediator for immediate event publication.</param>
    public EventMediator(IMessageMediator messageMediator)
    {
        ArgumentNullException.ThrowIfNull(messageMediator);

        _messageMediator = messageMediator;
    }

    /// <inheritdoc />
    public Task PublishAsync(IEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        eventMediationSettings ??= new EventMediationSettings();

        var mediationStrategy = new AsyncBroadcastMediationStrategy<IEvent>(eventMediationSettings);
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event, new MessageMediationRequest<IEvent, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = resolveStrategy,
            Tags = eventMediationSettings.Routing.Tags,
            Items = eventMediationSettings.Items,
            RegisterPlainMessagesOnSpot = eventMediationSettings.AutoRegisterUnregisteredMessageTypes,
            HandlerPredicate = handlerDescriptor => eventMediationSettings.Routing.HandlerPredicate(handlerDescriptor)
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);

        eventMediationSettings ??= new EventMediationSettings();

        var mediationStrategy = new AsyncBroadcastMediationStrategy<TEvent>(eventMediationSettings);
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event, new MessageMediationRequest<TEvent, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = resolveStrategy,
            Tags = eventMediationSettings.Routing.Tags,
            Items = eventMediationSettings.Items,
            RegisterPlainMessagesOnSpot = eventMediationSettings.AutoRegisterUnregisteredMessageTypes,
            HandlerPredicate = handlerDescriptor => eventMediationSettings.Routing.HandlerPredicate(handlerDescriptor)
        }, cancellationToken);
    }
}