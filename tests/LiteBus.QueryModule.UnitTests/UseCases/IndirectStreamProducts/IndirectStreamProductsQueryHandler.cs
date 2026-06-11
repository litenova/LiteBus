using System.Runtime.CompilerServices;
using LiteBus.Queries.Abstractions;
using LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

namespace LiteBus.QueryModule.UnitTests.UseCases.IndirectStreamProducts;

/// <summary>
///     Handles any stream query through the shared stream query interface.
/// </summary>
public sealed class IndirectStreamProductsQueryHandler
    : IStreamQueryHandler<IStreamQuery<StreamProductsQueryResult>, StreamProductsQueryResult>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<StreamProductsQueryResult> StreamAsync(
        IStreamQuery<StreamProductsQueryResult> message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (message is IAuditableQuery auditable)
        {
            auditable.ExecutedTypes.Add(GetType());
        }

        yield return await Task.FromResult(new StreamProductsQueryResult
        {
            CorrelationId = message is IndirectStreamProductsQuery query ? query.CorrelationId : Guid.Empty
        });
    }
}