using LiteBus.Events.Abstractions;
using LiteBus.Samples.Events;

namespace LiteBus.Samples.Commands;

public sealed class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    public async Task HandleAsync(OrderConfirmedEvent @event, CancellationToken cancellationToken)
    {
        // Simulate triggering fulfillment process
        await Task.Delay(50, cancellationToken);
        Console.WriteLine(
            $"[OrderConfirmedEventHandler] Order {{{@event.OrderId}}} confirmed at {{{@event.ConfirmedAt}}}. Starting fulfillment...");
    }
}