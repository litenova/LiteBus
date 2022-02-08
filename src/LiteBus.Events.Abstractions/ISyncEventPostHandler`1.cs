using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TEvent" /> post-handle phase
/// </summary>
public interface ISyncEventPostHandler<in TEvent> : IEventPostHandlerBase, ISyncPostHandler<TEvent>
    where TEvent : IEvent
{
}