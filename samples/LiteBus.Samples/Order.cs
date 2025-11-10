namespace LiteBus.Samples;

public sealed class Order
{
    public Guid Id { get; }
    public string CustomerId { get; }
    public List<OrderLineItem> LineItems { get; }
    public OrderStatus Status { get; }
    public decimal TotalAmount { get; }
    public DateTime CreatedAt { get; }

    public Order(string customerId, List<OrderLineItem> lineItems)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        LineItems = lineItems;
        Status = OrderStatus.Pending;
        TotalAmount = CalculateTotal();
        CreatedAt = DateTime.UtcNow;
    }
    
    private decimal CalculateTotal() => LineItems.Sum(x => x.Subtotal);
}