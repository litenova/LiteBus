using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents a query
    /// </summary>
    /// <typeparam name="TQueryResult">The result type of query</typeparam>
    public interface IStreamQuery<out TQueryResult> : IMessage<IAsyncEnumerable<TQueryResult>>
    {
    }
}