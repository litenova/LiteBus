namespace LiteBus.Samples.Documentation.Pipeline;

/// <summary>
///     Wraps a query result with envelope metadata.
/// </summary>
/// <typeparam name="T">The wrapped result type.</typeparam>
/// <param name="Value">The wrapped result.</param>
public sealed record Envelope<T>(T? Value);
