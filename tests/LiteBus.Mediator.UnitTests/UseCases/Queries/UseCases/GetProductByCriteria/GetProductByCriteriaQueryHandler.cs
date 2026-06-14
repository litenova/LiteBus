using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProductByCriteria;

public class GetProductByCriteriaQueryHandler<TPayload> : IQueryHandler<GetProductByCriteriaQuery<TPayload>, GetProductByCriteriaQueryResult>
{
    public Task<GetProductByCriteriaQueryResult> HandleAsync(GetProductByCriteriaQuery<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.FromResult(new GetProductByCriteriaQueryResult
        {
            CorrelationId = message.CorrelationId
        });
    }
}