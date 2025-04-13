using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.StreamProducts;

[HandlerOrder(1)]
public sealed class StreamProductsQueryHandlerPostHandler1 : IQueryPostHandler<StreamProductsQuery>
{
    public Task PostHandleAsync(StreamProductsQuery message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}