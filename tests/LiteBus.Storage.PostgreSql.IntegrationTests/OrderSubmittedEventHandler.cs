using LiteBus.Events.Abstractions;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

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
