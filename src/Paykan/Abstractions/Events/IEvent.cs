using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Abstractions
{
    /// <summary>
    /// An event is a notification sent by an object to signal the occurrence of an action.
    /// </summary>
    public interface IEvent : IMessage<Task>
    {
        
    }
}