using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on each event pre-handle phase
/// </summary>
public interface ISyncEventPreHandler : IEventPreHandlerBase, ISyncPreHandler<IEvent>
{
}