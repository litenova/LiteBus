using LiteBus.Events.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

internal sealed record OrderSubmittedIntegrationEvent
{
    public required Guid OrderId { get; init; }
}

internal sealed class OrderSubmittedEventHandler : IEventHandler<OrderSubmittedIntegrationEvent>
{
    private readonly EventRecorder _recorder;

    public OrderSubmittedEventHandler(EventRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task HandleAsync(OrderSubmittedIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record(message);
        return Task.CompletedTask;
    }
}

internal sealed class EventRecorder
{
    private readonly List<OrderSubmittedIntegrationEvent> _events = [];

    public IReadOnlyList<OrderSubmittedIntegrationEvent> Events => _events;

    public void Record(OrderSubmittedIntegrationEvent @event)
    {
        _events.Add(@event);
    }
}

internal sealed class AlwaysFailingOutboxDispatcher : IOutboxDispatcher
{
    public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated dispatcher failure.");
    }
}