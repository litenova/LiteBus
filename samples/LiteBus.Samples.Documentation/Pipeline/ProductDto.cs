namespace LiteBus.Samples.Documentation.Pipeline;

/// <summary>
///     Carries the product fields read by the documentation query snippet.
/// </summary>
/// <param name="Id">The product identifier.</param>
/// <param name="Name">The product name.</param>
/// <param name="Price">The product price.</param>
public sealed record ProductDto(Guid Id, string Name, decimal Price);
