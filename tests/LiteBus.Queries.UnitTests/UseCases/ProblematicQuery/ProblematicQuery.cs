using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.ProblematicQuery;

public sealed class ProblematicQuery : IAuditableQuery, IQuery<ProblematicQueryResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public required Type ThrowExceptionInType { get; init; }

    public List<Type> ExecutedTypes { get; } = new();
}