namespace Paykan.Events.Abstraction
{
    public interface IEventPostHandleHook : IPostHandleHook<IEvent>
    {
    }

    public interface IEventPostHandleHook<in TEvent> : IPostHandleHook<TEvent> where TEvent : IEvent
    {
    }
}