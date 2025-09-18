using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.GetProduct;

[HandlerPriority(1)]
public sealed class GetProductQueryHandlerPostHandler1 : IQueryPostHandler<GetProductQuery, GetProductQueryResult>
{
    public Task PostHandleAsync(GetProductQuery message, GetProductQueryResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}