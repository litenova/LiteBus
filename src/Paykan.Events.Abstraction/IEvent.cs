using System.Threading.Tasks;

namespace Paykan.Events.Abstraction
{
    /// <summary>
    /// An event is a notification sent by an object to signal the occurrence of an action.
    /// </summary>
    public interface IEvent : IMessage<Task>
    {
        
    }
}