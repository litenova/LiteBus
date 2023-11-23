using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Events.MediationStrategies;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events;

/// <inheritdoc cref="IEventMediator" />
public sealed class EventMediator : IEventPublisher
{
    private readonly IMessageMediator _messageMediator;

    public EventMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task PublishAsync(IEvent @event, EventMediationSettings settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= new EventMediationSettings();

        var mediationStrategy = new AsyncBroadcastMediationStrategy<IEvent>(settings);

        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event,
            new MediateOptions<IEvent, Task>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken
            });
    }

    public Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= new EventMediationSettings();
        
        var mediationStrategy = new AsyncBroadcastMediationStrategy<TEvent>(settings);

        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event,
            new MediateOptions<TEvent, Task>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken
            });
    }
}