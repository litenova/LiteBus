using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.EventWithTag;

public sealed class EventWithTag : IAuditableEvent, IEvent
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}