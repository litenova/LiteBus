using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

public interface ISyncQueryErrorHandler : IQueryHandler, ISyncErrorHandler<IQuery>
{
}