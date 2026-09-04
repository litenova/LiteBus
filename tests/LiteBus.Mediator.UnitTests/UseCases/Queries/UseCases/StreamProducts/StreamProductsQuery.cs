using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;

public sealed class StreamProductsQuery : IAuditableQuery, IStreamQuery<StreamProductsQueryResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public bool AnswerFromShortcut { get; init; }

    public int? RetrievedStreamCount { get; set; }

    public List<Type> ExecutedTypes { get; } = new();
}