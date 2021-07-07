using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents an asynchronous query handler returning <see cref="IAsyncEnumerable{TQueryResult}"/>
    /// </summary>
    /// <typeparam name="TQuery">Type of query</typeparam>
    /// <typeparam name="TQueryResult">Type of query result</typeparam>
    public interface
        IStreamQueryHandler<in TQuery, out TQueryResult> : IAsyncEnumerableMessageHandler<TQuery, TQueryResult>
        where TQuery : IStreamQuery<TQueryResult>
    {
    }
}