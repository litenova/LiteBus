namespace LiteBus.EventModule.UnitTests.UseCases;

public interface IAuditableEvent
{
    public List<Type> ExecutedTypes { get; }
}