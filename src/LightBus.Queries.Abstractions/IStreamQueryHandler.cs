using System.Collections.Generic;
using LightBus.Messaging.Abstractions;

namespace LightBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents the definition of a handler that handles a query with streaming result
    /// </summary>
    /// <typeparam name="TQuery">Type of query</typeparam>
    /// <typeparam name="TResult">Type of query result</typeparam>
    public interface IStreamQueryHandler<in TQuery, out TResult> : IMessageHandler<TQuery, IAsyncEnumerable<TResult>>
        where TQuery : IStreamQuery<TResult>
    {
    }
}