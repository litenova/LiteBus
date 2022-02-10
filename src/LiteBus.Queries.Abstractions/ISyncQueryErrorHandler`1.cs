using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

public interface ISyncQueryErrorHandler<in TQuery> : IQueryErrorHandlerBase, ISyncErrorHandler<TQuery>
    where TQuery : IQueryBase
{
}