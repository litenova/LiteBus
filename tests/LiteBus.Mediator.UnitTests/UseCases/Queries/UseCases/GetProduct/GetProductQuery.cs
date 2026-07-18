using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProduct;

public sealed class GetProductQuery : IAuditableQuery, IQuery<GetProductQueryResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}