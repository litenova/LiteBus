using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;

public sealed class StreamProductsQueryHandlerPreHandler : IQueryShortCircuitingPreHandler<StreamProductsQuery>
{
    public Task<PipelineDirective> PreHandleAsync(StreamProductsQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ExecutedTypes.Add(GetType());

        return message.AbortInPreHandler
            ? Task.FromResult(PipelineDirective.ShortCircuit(reason: "aborted by pre-handler"))
            : Task.FromResult(PipelineDirective.Continue);
    }
}
