using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.GetProduct;

[HandlerOrder(2)]
public sealed class GetProductQueryHandlerPostHandler2 : IQueryPostHandler<GetProductQuery>
{
    public Task PostHandleAsync(GetProductQuery message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}