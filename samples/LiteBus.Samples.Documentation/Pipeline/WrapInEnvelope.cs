// <docs-snippet pipeline-query-post-handler>
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Samples.Documentation.Pipeline;

/// <summary>
///     Wraps a product query result in an envelope after the handler returns.
/// </summary>
public sealed class WrapInEnvelope : IQueryPostHandler<GetProductByIdQuery, ProductDto>
{
    /// <inheritdoc />
    public Task PostHandleAsync(GetProductByIdQuery message, ProductDto? messageResult, CancellationToken cancellationToken = default)
    {
        AmbientExecutionContext.Current.MessageResult = new Envelope<ProductDto>(messageResult);
        return Task.CompletedTask;
    }
}
// </docs-snippet>
