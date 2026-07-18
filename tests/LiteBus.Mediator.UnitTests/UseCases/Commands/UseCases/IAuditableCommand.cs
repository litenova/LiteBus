namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases;

public interface IAuditableCommand
{
    public List<Type> ExecutedTypes { get; }
}