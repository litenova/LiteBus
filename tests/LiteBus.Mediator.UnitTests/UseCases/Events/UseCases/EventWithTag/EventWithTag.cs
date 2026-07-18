using LiteBus.Events.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.EventWithTag;

public sealed class EventWithTag : IAuditableEvent, IEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}