namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases;

public interface IAuditableEvent
{
    public List<Type> ExecutedTypes { get; }
}