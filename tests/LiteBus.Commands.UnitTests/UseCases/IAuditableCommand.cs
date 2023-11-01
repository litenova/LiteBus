namespace LiteBus.Commands.UnitTests.UseCases;

public interface IAuditableCommand
{
    public List<Type> ExecutedTypes { get; }
}