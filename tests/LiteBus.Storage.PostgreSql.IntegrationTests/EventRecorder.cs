namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal sealed class EventRecorder
{
    private readonly List<OrderSubmittedIntegrationEvent> _events = [];

    public IReadOnlyList<OrderSubmittedIntegrationEvent> Events => _events;

    public void Record(OrderSubmittedIntegrationEvent @event) => _events.Add(@event);
}
