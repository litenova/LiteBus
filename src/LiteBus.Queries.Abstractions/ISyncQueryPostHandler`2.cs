using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TQuery" /> post-handle phase
/// </summary>
public interface ISyncQueryPostHandler<in TQuery, in TQueryResult> : IQueryPostHandlerBase,
                                                                     ISyncPostHandler<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>
{
}