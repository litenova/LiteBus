using System.Collections.Generic;
using System.Threading.Tasks;

namespace BasicBus.Abstractions
{
    public interface IQueryHandler<in TQuery, TQueryResult> where TQuery : IQuery<TQueryResult>
    {
        Task<TQueryResult> HandleAsync(TQuery query);
    }

    public interface IStreamQueryHandler<in TQuery, out TQueryResult> where TQuery : IQuery<TQueryResult>
    {
        IAsyncEnumerable<TQueryResult> HandleAsync(TQuery query);
    }
}