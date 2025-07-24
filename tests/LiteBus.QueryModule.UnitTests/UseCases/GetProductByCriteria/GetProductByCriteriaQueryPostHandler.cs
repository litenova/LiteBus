using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.GetProductByCriteria;

public class GetProductByCriteriaQueryPostHandler<TPayload> : IQueryPostHandler<GetProductByCriteriaQuery<TPayload>>
{
    public Task PostHandleAsync(GetProductByCriteriaQuery<TPayload> message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}