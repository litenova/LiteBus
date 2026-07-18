using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProductByCriteria;

public sealed class GetProductByCriteriaQuery<TPayload> : IAuditableQuery, IQuery<GetProductByCriteriaQueryResult>
{
    public required TPayload Payload { get; init; }

    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}