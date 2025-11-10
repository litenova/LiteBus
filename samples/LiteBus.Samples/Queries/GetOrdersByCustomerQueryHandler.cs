using LiteBus.Queries.Abstractions;

namespace LiteBus.Samples.Queries;

public sealed class GetOrdersByCustomerQueryHandler : IQueryHandler<GetOrdersByCustomerQuery, IEnumerable<OrderDto>>
{
    public async Task<IEnumerable<OrderDto>> HandleAsync(GetOrdersByCustomerQuery query, CancellationToken cancellationToken)
    {
        var order1 = new OrderDto(Guid.NewGuid(), query.CustomerId, 49.99m, OrderStatus.Confirmed, DateTime.UtcNow.AddDays(-10),
        [
            new OrderLineItemDto(Guid.NewGuid(), 2, 19.99m),
            new OrderLineItemDto(Guid.NewGuid(), 1, 9.99m)
        ]);
        var order2 = new OrderDto(Guid.NewGuid(), query.CustomerId, 79.99m, OrderStatus.Shipped, DateTime.UtcNow.AddDays(-5),
        [
            new OrderLineItemDto(Guid.NewGuid(), 3, 19.99m),
            new OrderLineItemDto(Guid.NewGuid(), 2, 9.99m)
        ]);

        return await Task.FromResult(new List<OrderDto> { order1, order2 });
    }
}