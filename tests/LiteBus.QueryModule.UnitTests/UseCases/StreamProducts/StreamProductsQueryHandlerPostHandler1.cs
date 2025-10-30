using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

[HandlerPriority(1)]
public sealed class StreamProductsQueryHandlerPostHandler1 : IStreamQueryPostHandler<StreamProductsQuery, StreamProductsQueryResult>
{
    public Task PostHandleAsync(StreamProductsQuery message,
                                IAsyncEnumerable<StreamProductsQueryResult>? messageResult,
                                CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        // Retrieve the metadata that the main handler placed in the execution context.
        if (AmbientExecutionContext.HasCurrent &&
            AmbientExecutionContext.Current.Items.TryGetValue("StreamCount", out var countValue) &&
            countValue is int count)
        {
            message.RetrievedStreamCount = count;
        }

        return Task.CompletedTask;
    }
}