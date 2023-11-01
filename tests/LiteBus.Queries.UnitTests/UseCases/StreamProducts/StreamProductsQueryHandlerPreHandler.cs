using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.StreamProducts;

public sealed class StreamProductsQueryHandlerPreHandler : IQueryPreHandler<StreamProductsQuery>
{
    public Task PreHandleAsync(StreamProductsQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}