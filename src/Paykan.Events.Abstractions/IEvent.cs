using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Events.Abstractions
{
    /// <summary>
    ///     Represents an event
    /// </summary>
    public interface IEvent : IMessage<Task>
    {
    }
}