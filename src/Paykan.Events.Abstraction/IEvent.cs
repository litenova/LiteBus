using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Events.Abstraction
{
    /// <summary>
    /// Represents an event
    /// </summary>
    public interface IEvent : IMessage<Task>
    {
        
    }
}