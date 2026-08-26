using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;

public sealed class StreamProductsQueryHandlerPreHandler : IStreamQueryGate<StreamProductsQuery, StreamProductsQueryResult>
{
    public Task<PipelineDirective<IAsyncEnumerable<StreamProductsQueryResult>>> DecideAsync(
        StreamProductsQuery message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ExecutedTypes.Add(GetType());

        // Stopping a stream without supplying one is a legitimate answer: the caller enumerates nothing.
        return Task.FromResult(message.ShortCircuitInGate
            ? PipelineDirective<IAsyncEnumerable<StreamProductsQueryResult>>.ShortCircuit(
                AsyncEnumerable.Empty<StreamProductsQueryResult>(),
                "answered by the gate")
            : PipelineDirective<IAsyncEnumerable<StreamProductsQueryResult>>.Continue);
    }
}
