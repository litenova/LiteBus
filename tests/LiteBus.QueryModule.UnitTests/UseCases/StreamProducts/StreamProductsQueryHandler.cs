using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

public sealed class StreamProductsQueryHandler : IStreamQueryHandler<StreamProductsQuery, StreamProductsQueryResult>
{
    public IAsyncEnumerable<StreamProductsQueryResult> StreamAsync(StreamProductsQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return new List<StreamProductsQueryResult>
        {
            new()
            {
                CorrelationId = message.CorrelationId
            }
        }.ToAsyncEnumerable();
    }
}