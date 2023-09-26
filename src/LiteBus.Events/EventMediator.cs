using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events;

/// <inheritdoc cref="IEventMediator" />
public class EventMediator : IEventPublisher
{
    private readonly IMessageMediator _messageMediator;

    public EventMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new AsyncBroadcastMediationStrategy<IEvent>();

        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event,
            new MediateOptions<IEvent, Task>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken
            });
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new AsyncBroadcastMediationStrategy<TEvent>();

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