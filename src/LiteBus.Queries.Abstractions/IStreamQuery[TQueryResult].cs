namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a query
/// </summary>
/// <typeparam name="TQueryResult">The result type of query</typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IStreamQuery<out TQueryResult> : IQueryBase
{
}