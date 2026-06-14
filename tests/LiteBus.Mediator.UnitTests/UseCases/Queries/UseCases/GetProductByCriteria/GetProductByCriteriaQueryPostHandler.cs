using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProductByCriteria;

public class GetProductByCriteriaQueryPostHandler<TPayload> : IQueryPostHandler<GetProductByCriteriaQuery<TPayload>>
{
    public Task PostHandleAsync(GetProductByCriteriaQuery<TPayload> message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}