namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Carries the order fields staged by the documentation enqueue snippet.
/// </summary>
/// <param name="Id">The order identifier.</param>
public sealed record Order(Guid Id);
