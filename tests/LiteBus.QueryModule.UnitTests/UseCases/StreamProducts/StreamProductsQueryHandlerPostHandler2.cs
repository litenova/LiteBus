using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

[HandlerPriority(2)]
public sealed class StreamProductsQueryHandlerPostHandler2 : IStreamQueryPostHandler<StreamProductsQuery, StreamProductsQueryResult>
{
    public Task PostHandleAsync(StreamProductsQuery message,
                                IAsyncEnumerable<StreamProductsQueryResult>? messageResult,
                                CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}