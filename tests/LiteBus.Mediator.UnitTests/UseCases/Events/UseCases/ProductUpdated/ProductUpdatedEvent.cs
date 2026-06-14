namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.ProductUpdated;

public sealed record ProductUpdatedEvent : IAuditableEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}