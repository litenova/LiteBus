using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.GetProduct;

public sealed class GetProductQuery : IAuditableQuery, IQuery<GetProductQueryResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}