namespace LiteBus.Samples.Queries;

public sealed record OrderDto(
    Guid Id,
    string CustomerId,
    decimal TotalAmount,
    OrderStatus Status,
    DateTime CreatedAt,
    List<OrderLineItemDto> LineItems);