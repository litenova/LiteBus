using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an synchronous event handler
/// </summary>
/// <typeparam name="TEvent">The type of event</typeparam>
public interface ISyncEventHandler<in TEvent> : IEventHandlerBase, ISyncHandler<TEvent> where TEvent : IEvent
{
}