namespace LiteBus.Events.UnitTests.UseCases.ProductUpdated;

public sealed class ProductUpdatedEvent : IAuditableEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}