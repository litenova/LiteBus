using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Abstractions
{
    /// <summary>
    /// The handler to handle the events
    /// </summary>
    /// <typeparam name="TEvent">The type of event to be handled</typeparam>
    public interface IEventHandler<in TEvent> : IMessageHandler<TEvent, Task> where TEvent : IEvent
    {
        
    }
}