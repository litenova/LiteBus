using LiteBus.Queries.Abstractions;
using LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;

namespace LiteBus.QueryModule.UnitTests.UseCases.IndirectStreamProducts;

/// <summary>
///     A concrete stream query handled only by an interface-registered handler.
/// </summary>
public sealed class IndirectStreamProductsQuery : IAuditableQuery, IStreamQuery<StreamProductsQueryResult>
{
    /// <inheritdoc />
    public Guid CorrelationId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public List<Type> ExecutedTypes { get; } = [];
}