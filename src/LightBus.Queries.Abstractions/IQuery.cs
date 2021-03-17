using System.Threading.Tasks;
using LightBus.Messaging.Abstractions;

namespace LightBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents a query
    /// </summary>
    /// <typeparam name="TQueryResult">The result type of query</typeparam>
    public interface IQuery<TQueryResult> : IMessage<Task<TQueryResult>>
    {
    }
}