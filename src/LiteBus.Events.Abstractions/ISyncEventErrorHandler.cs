using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on each event error-handle phase
/// </summary>
public interface ISyncEventErrorHandler : IEventErrorHandlerBase, ISyncErrorHandler<IEvent>
{
}