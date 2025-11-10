using LiteBus.Queries.Abstractions;

namespace LiteBus.Samples.Queries;

public sealed class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        var order = new OrderDto(Guid.NewGuid(), "customer-123", 99.99m, OrderStatus.Pending, DateTime.UtcNow,
        [
            new OrderLineItemDto(Guid.NewGuid(), 1, 49.99m),
            new OrderLineItemDto(Guid.NewGuid(), 2, 24.99m)
        ]);

        return Task.FromResult(order);
    }
}