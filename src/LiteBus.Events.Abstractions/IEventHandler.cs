using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

public interface IEventHandlerBase : IEventConstruct
{
}

/// <summary>
///     Represents an asynchronous event handler
/// </summary>
/// <typeparam name="TEvent">The type of event</typeparam>
public interface IEventHandler<in TEvent> : IEventHandlerBase, IAsyncMessageHandler<TEvent> where TEvent : IEvent
{
}