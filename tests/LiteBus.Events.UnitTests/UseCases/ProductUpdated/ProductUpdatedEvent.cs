namespace LiteBus.Events.UnitTests.UseCases.ProductUpdated;

public sealed class ProductUpdatedEvent : IAuditableEvent
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}