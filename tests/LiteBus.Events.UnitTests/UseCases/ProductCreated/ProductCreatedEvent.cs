using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductCreated;

public sealed class ProductCreatedEvent : IAuditableEvent, IEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}