using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Discovery;
using LiteBus.Messaging.Workflows.Execution.Handle;
using LiteBus.Messaging.Workflows.Resolution.Lazy;

namespace LiteBus.Events;

/// <inheritdoc cref="IEventMediator" />
public class EventMediator : IEventPublisher
{
    private readonly IMediator _mediator;

    public EventMediator(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        var executionWorkflow = new AsyncBroadcastExecutionWorkflow<IEvent>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        await _mediator.Mediate(@event, findStrategy, new LazyResolutionWorkflow(), executionWorkflow);
    }

    public void Publish(IEvent @event)
    {
        var executionWorkflow = new SyncBroadcastExecutionWorkflow<IEvent>();

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        _mediator.Mediate(@event, findStrategy, new LazyResolutionWorkflow(), executionWorkflow);
    }
}