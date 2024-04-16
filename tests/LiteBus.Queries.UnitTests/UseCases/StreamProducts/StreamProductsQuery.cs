using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.StreamProducts;

public sealed class StreamProductsQuery : IAuditableQuery, IStreamQuery<StreamProductsQueryResult>
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
    
    public bool AbortInPreHandler { get; set; }
}