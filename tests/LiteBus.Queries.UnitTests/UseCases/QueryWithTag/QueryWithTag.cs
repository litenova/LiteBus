using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.QueryWithTag;

public sealed class QueryWithTag : IAuditableQuery, IQuery<QueryWithTagResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}