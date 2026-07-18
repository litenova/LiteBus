using System.Runtime.CompilerServices;
using LiteBus.Queries.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.IndirectStreamProducts;

/// <summary>
///     Handles the indirect stream products query type directly.
/// </summary>
public sealed class IndirectStreamProductsQueryHandler
    : IStreamQueryHandler<IndirectStreamProductsQuery, StreamProductsQueryResult>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<StreamProductsQueryResult> StreamAsync(
        IndirectStreamProductsQuery message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        yield return await Task.FromResult(new StreamProductsQueryResult
        {
            CorrelationId = message.CorrelationId
        }).ConfigureAwait(false);
    }
}
