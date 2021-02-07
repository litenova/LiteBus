using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Events.Abstraction
{
    /// <summary>
    ///     Represents the definition of a handler that handles a event
    /// </summary>
    /// <typeparam name="TEvent">The type of event</typeparam>
    public interface IEventHandler<in TEvent> : IMessageHandler<TEvent, Task> where TEvent : IEvent
    {
    }
}