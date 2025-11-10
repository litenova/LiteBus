namespace LiteBus.Samples.Events;

public sealed record OrderConfirmedEvent(Guid OrderId, DateTime ConfirmedAt);