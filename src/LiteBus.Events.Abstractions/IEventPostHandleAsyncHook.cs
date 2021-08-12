using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each event post-handle phase
    /// </summary>
    public interface IEventPostHandleAsyncHook : IPostHandleAsyncHook<IEvent>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TEvent" /> post-handle phase
    /// </summary>
    public interface IEventPostHandleAsyncHook<in TEvent> : IPostHandleAsyncHook<TEvent> where TEvent : IEvent
    {
    }
}