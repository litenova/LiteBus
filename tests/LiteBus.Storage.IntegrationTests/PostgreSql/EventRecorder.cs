using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed class EventRecorder
{
    private readonly List<OrderSubmittedIntegrationEvent> _events = [];

    public IReadOnlyList<OrderSubmittedIntegrationEvent> Events => _events;

    public void Record(OrderSubmittedIntegrationEvent @event)
    {
        _events.Add(@event);
    }
}