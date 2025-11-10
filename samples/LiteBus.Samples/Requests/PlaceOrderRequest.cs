namespace LiteBus.Samples.Requests;

public sealed record PlaceOrderRequest(string CustomerId, List<PlaceOrderLineItemRequest> LineItems);