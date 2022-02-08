using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an action that is executed on each query error-handle phase
/// </summary>
public interface IQueryErrorHandler : IQueryErrorHandlerBase, IAsyncErrorHandler<IQueryBase>
{
}