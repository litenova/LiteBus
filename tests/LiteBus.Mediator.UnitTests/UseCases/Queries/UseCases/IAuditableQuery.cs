namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases;

public interface IAuditableQuery
{
    public List<Type> ExecutedTypes { get; }
}