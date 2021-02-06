using System.Collections.Generic;
using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Queries.Abstraction
{
    public interface IQueryHandler<in TQuery, TResult> : IMessageHandler<TQuery, Task<TResult>> 
        where TQuery : IQuery<TResult>
    {
    }

    public interface IStreamQueryHandler<in TQuery, out TResult> : IMessageHandler<TQuery, IAsyncEnumerable<TResult>>
        where TQuery : IStreamQuery<TResult>
    {
    }
}