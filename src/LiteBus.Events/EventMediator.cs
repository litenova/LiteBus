using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Discovery;
using LiteBus.Messaging.Workflows.Execution;

namespace LiteBus.Events;

/// <inheritdoc cref="IEventMediator" />
public class EventMediator : IEventPublisher
{
    private readonly IMessageMediator _messageMediator;

    public EventMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new AsyncBroadcastMediationStrategy<IEvent>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        await _messageMediator.Mediate(@event, findStrategy, mediationStrategy);
    }
}