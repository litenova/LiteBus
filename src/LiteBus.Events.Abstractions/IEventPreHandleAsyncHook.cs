using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each event pre-handle phase
    /// </summary>
    public interface IEventPreHandleAsyncHook : IPreHandleAsyncHook<IEvent>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TEvent" /> pre-handle phase
    /// </summary>
    public interface IEventPreHandleAsyncHook<in TEvent> : IPreHandleAsyncHook<TEvent> where TEvent : IEvent
    {
    }
}