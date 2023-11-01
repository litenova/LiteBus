namespace LiteBus.Events.UnitTests.UseCases;

public interface IAuditableEvent
{
    public List<Type> ExecutedTypes { get; }
}