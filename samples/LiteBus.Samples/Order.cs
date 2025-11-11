namespace LiteBus.Samples;

public sealed class Order
{
    public Order(string customerId, List<OrderLineItem> lineItems)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        LineItems = lineItems;
        Status = OrderStatus.Pending;
        TotalAmount = CalculateTotal();
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; }

    public string CustomerId { get; }

    public List<OrderLineItem> LineItems { get; }

    public OrderStatus Status { get; }

    public decimal TotalAmount { get; }

    public DateTime CreatedAt { get; }

    private decimal CalculateTotal()
    {
        return LineItems.Sum(x => x.Subtotal);
    }
}