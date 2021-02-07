using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Queries.Abstractions
{
    /// <summary>
    ///     Represents the definition of a handler that handles a query
    /// </summary>
    /// <typeparam name="TQuery">Type of query</typeparam>
    /// <typeparam name="TResult">Type of query result</typeparam>
    public interface IQueryHandler<in TQuery, TResult> : IMessageHandler<TQuery, Task<TResult>>
        where TQuery : IQuery<TResult>
    {
    }
}