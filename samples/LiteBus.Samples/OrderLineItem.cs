namespace LiteBus.Samples;

public sealed class OrderLineItem
{
    public Guid ProductId { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal Subtotal => Quantity * UnitPrice;

    public OrderLineItem(Guid productId, int quantity, decimal unitPrice)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}