using System.Threading.Tasks;
using LightBus.Messaging.Abstractions;

namespace LightBus.Events.Abstractions
{
    /// <summary>
    ///     Represents an event
    /// </summary>
    public interface IEvent : IMessage<Task>
    {
    }
}