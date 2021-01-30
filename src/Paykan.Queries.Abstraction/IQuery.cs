using System.Collections.Generic;
using System.Threading.Tasks;

namespace Paykan.Queries.Abstraction
{
    /// <summary>
    /// The root of all queries
    /// </summary>
    public interface IBaseQuery : IMessage
    {
        
    }
    
    /// <summary>
    /// Represents a query that is intended to return data
    /// </summary>
    /// <typeparam name="TQueryResult">The result type of query</typeparam>
    public interface IQuery<TQueryResult> : IMessage<Task<TQueryResult>>
    {
        
    }

    /// <summary>
    /// Represents a query that is intended to return streaming data
    /// </summary>
    /// <typeparam name="TQueryResult">The result type of query</typeparam>
    public interface IStreamQuery<out TQueryResult> : IMessage<IAsyncEnumerable<TQueryResult>>
    {
        
    }
}