using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

public interface ISyncQueryErrorHandler : IQueryErrorHandlerBase, ISyncErrorHandler<IQueryBase>
{
}