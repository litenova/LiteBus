namespace LiteBus.Samples.Requests;

public sealed record PlaceOrderLineItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);