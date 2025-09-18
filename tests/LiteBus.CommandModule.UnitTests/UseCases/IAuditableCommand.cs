namespace LiteBus.CommandModule.UnitTests.UseCases;

public interface IAuditableCommand
{
    public List<Type> ExecutedTypes { get; }
}