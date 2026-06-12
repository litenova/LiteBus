using LiteBus.Events.Abstractions;

namespace LiteBus.Samples.Events;

public sealed class OrderPlacedEventHandler : IEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"[OrderPlacedEventHandler] Order {{{@event.OrderId}}} consumed.");
    }
}