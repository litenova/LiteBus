using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.GetProductByCriteria;

public class GetProductByCriteriaQueryPreHandler<TPayload> : IQueryPreHandler<GetProductByCriteriaQuery<TPayload>>
{
    public Task PreHandleAsync(GetProductByCriteriaQuery<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}