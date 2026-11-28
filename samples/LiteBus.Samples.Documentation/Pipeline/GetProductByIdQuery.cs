using LiteBus.Queries.Abstractions;

namespace LiteBus.Samples.Documentation.Pipeline;

/// <summary>
///     Reads one product by its identifier.
/// </summary>
/// <param name="Id">The product identifier.</param>
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;
