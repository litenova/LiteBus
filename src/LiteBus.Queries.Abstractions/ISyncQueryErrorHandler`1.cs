using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

public interface ISyncQueryErrorHandler<in TQuery> : IQueryHandler, ISyncErrorHandler<TQuery>
    where TQuery : IQuery
{
}