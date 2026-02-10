using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Commands;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    public Task<Guid> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var lineItems = command.LineItems
            .Select(x => new OrderLineItem(x.ProductId, x.Quantity, x.UnitPrice))
            .ToList();

        var order = new Order(command.CustomerId, lineItems);

        Console.WriteLine(
            $"[PlaceOrderCommandHandler] Order {{{order.Id}}} placed at {{{order.CreatedAt}}}.");

        return Task.FromResult(order.Id);
    }
}