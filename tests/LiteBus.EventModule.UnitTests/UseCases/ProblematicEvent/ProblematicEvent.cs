using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProblematicEvent;

public sealed class ProblematicEvent : IAuditableEvent, IEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public required Type ThrowExceptionInType { get; init; }

    public List<Type> ExecutedTypes { get; } = new();
}