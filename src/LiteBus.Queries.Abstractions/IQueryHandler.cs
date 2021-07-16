using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents an asynchronous query handler returning <typeparamref name="TQueryResult"/>
    /// </summary>
    /// <typeparam name="TQuery">Type of query</typeparam>
    /// <typeparam name="TQueryResult">Type of query result</typeparam>
    public interface IQueryHandler<TQuery, TQueryResult> : IAsyncMessageHandler<TQuery, TQueryResult> where TQuery : IQuery<TQueryResult>
    {
    }
}