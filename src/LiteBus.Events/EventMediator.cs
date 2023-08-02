using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.FindStrategies;
using LiteBus.Messaging.Abstractions.MediationStrategies;

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
        var mediationStrategy = new AsyncBroadcastMediationStrategy<IEvent>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event, findStrategy, mediationStrategy);
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new AsyncBroadcastMediationStrategy<TEvent>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(@event, findStrategy, mediationStrategy);
    }
}