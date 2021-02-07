using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Queries.Abstractions
{
    /// <summary>
    ///     Represents a query
    /// </summary>
    /// <typeparam name="TQueryResult">The result type of query</typeparam>
    public interface IQuery<TQueryResult> : IMessage<Task<TQueryResult>>
    {
    }
}