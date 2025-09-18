namespace LiteBus.EventModule.UnitTests.UseCases.ProductUpdated;

public record class ProductUpdatedEvent : IAuditableEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}