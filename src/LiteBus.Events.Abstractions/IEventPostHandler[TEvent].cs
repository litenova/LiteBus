using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TEvent" /> post-handle phase
/// </summary>
public interface IEventPostHandler<in TEvent> : IMessagePostHandler<TEvent>, IRegistrableEventConstruct where TEvent : IEvent
{
}