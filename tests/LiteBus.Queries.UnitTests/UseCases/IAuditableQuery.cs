namespace LiteBus.Queries.UnitTests.UseCases;

public interface IAuditableQuery
{
    public List<Type> ExecutedTypes { get; }
}