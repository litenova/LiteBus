namespace LiteBus.Samples.Queries;

public sealed record OrderLineItemDto(Guid ProductId, int Quantity, decimal UnitPrice);