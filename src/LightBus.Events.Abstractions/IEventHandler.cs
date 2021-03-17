using System.Threading.Tasks;
using LightBus.Messaging.Abstractions;

namespace LightBus.Events.Abstractions
{
    /// <summary>
    ///     Represents the definition of a handler that handles a event
    /// </summary>
    /// <typeparam name="TEvent">The type of event</typeparam>
    public interface IEventHandler<in TEvent> : IMessageHandler<TEvent, Task> where TEvent : IEvent
    {
    }
}