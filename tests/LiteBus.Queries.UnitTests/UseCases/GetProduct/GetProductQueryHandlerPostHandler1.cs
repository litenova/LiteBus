using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.GetProduct;

[HandlerOrder(1)]
public sealed class GetProductQueryHandlerPostHandler1 : IQueryPostHandler<GetProductQuery, GetProductQueryResult>
{
    public Task PostHandleAsync(GetProductQuery message, GetProductQueryResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}