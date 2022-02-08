using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an action that is executed on each query pre-handle phase
/// </summary>
public interface IQueryPreHandler : IQueryPreHandlerBase, IAsyncPreHandler<IQueryBase>
{
}