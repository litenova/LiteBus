namespace LiteBus.QueryModule.UnitTests.UseCases;

public interface IAuditableQuery
{
    public List<Type> ExecutedTypes { get; }
}