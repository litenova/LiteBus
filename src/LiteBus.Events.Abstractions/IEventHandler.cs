using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions
{
    /// <summary>
    ///     Represents the definition of a handler that handles a event
    /// </summary>
    /// <typeparam name="TEvent">The type of event</typeparam>
    public interface IEventHandler<in TEvent> : ISyncMessageHandler<TEvent, Task> where TEvent : IEvent
    {
    }
}