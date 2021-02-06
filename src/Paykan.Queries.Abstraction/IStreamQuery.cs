using System.Collections.Generic;
using Paykan.Messaging.Abstractions;

namespace Paykan.Queries.Abstraction
{
    /// <summary>
    ///     Represents a query
    /// </summary>
    /// <typeparam name="TQueryResult">The result type of query</typeparam>
    public interface IStreamQuery<out TQueryResult> : IMessage<IAsyncEnumerable<TQueryResult>>
    {
    }
}