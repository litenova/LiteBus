using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an synchronous query handler returning <typeparamref name="TQueryResult" />
/// </summary>
/// <typeparam name="TQuery">Type of query</typeparam>
/// <typeparam name="TQueryResult">Type of query result</typeparam>
public interface ISyncQueryHandler<in TQuery, out TQueryResult> : IQueryHandler, ISyncHandler<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>
{
}