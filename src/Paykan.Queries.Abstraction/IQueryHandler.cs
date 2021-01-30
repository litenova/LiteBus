using System.Collections.Generic;
using System.Threading.Tasks;

namespace Paykan.Queries.Abstraction
{
    public interface IQueryHandler<in TQuery, TQueryResult> : IMessageHandler<TQuery, Task<TQueryResult>>
        where TQuery : IQuery<TQueryResult>
    {
        
    }

    public interface IStreamQueryHandler<in TQuery, out TQueryResult>
        : IMessageHandler<TQuery, IAsyncEnumerable<TQueryResult>>
        where TQuery : IStreamQuery<TQueryResult>
    {
        
    }
}