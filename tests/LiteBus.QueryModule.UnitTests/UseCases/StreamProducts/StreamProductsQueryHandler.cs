using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

public sealed class StreamProductsQueryHandler : IStreamQueryHandler<StreamProductsQuery, StreamProductsQueryResult>
{
    public async IAsyncEnumerable<StreamProductsQueryResult> StreamAsync(StreamProductsQuery message,
                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        var results = new List<StreamProductsQueryResult> { new() { CorrelationId = message.CorrelationId } };
        var count = 0;

        foreach (var result in results)
        {
            yield return await Task.FromResult(result);
            count++;
        }
        
        // After the stream is fully yielded, place the calculated metadata into the execution context.
        // This is the recommended pattern for passing data to stream post-handlers.
        if (AmbientExecutionContext.HasCurrent)
        {
            AmbientExecutionContext.Current.Items["StreamCount"] = count;
        }
    }
}