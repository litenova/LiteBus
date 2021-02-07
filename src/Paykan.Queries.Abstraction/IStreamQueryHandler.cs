using System.Collections.Generic;
using Paykan.Messaging.Abstractions;

namespace Paykan.Queries.Abstraction
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