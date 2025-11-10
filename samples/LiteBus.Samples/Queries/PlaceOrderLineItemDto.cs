namespace LiteBus.Samples.Queries;

public sealed record PlaceOrderLineItemDto(Guid ProductId, int Quantity, decimal UnitPrice);