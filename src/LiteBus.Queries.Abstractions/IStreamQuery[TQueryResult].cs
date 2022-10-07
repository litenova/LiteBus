namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a query with streaming result
/// </summary>
/// <typeparam name="TQueryResult">The result type of query</typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IStreamQuery<out TQueryResult> : IQuery
{
}