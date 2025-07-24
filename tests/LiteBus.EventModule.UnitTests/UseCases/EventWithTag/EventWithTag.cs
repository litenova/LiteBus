using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.EventWithTag;

public sealed class EventWithTag : IAuditableEvent, IEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}