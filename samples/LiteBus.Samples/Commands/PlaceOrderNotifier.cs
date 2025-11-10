using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Samples.Events;

namespace LiteBus.Samples.Commands;

public sealed class PlaceOrderNotifier : ICommandPostHandler<PlaceOrderCommand, Guid>
{
    private readonly IEventPublisher _eventPublisher;

    public PlaceOrderNotifier(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task PostHandleAsync(PlaceOrderCommand command, Guid orderId, CancellationToken cancellationToken = default)
    {
        var @event = new OrderPlacedEvent(orderId);
        await _eventPublisher.PublishAsync(@event, cancellationToken: cancellationToken);

        Console.WriteLine(
            $"[PlaceOrderCommandHandler] Order {{{@event.OrderId}}} published at {{{DateTime.UtcNow}}}.");
    }
}