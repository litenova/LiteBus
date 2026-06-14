using LiteBus.Queries.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.IndirectStreamProducts;

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