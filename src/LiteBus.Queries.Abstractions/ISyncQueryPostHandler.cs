using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an action that is executed on each query post-handle phase
/// </summary>
public interface ISyncQueryPostHandler : IQueryPostHandlerBase, ISyncPostHandler<IQueryBase>
{
}