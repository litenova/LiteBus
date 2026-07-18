using LiteBus.Events.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

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