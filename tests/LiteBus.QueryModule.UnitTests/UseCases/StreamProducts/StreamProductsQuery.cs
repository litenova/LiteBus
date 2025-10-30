using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

public sealed class StreamProductsQuery : IAuditableQuery, IStreamQuery<StreamProductsQueryResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public bool AbortInPreHandler { get; init; }

    public List<Type> ExecutedTypes { get; } = new();

    public int? RetrievedStreamCount { get; set; }
}