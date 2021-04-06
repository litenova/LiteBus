using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Mediates a query to its corresponding handler
    /// </summary>
    public interface IQueryMediator
    {
        Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> command);
        IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query);
    }
}