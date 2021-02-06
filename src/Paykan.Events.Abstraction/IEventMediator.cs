using System.Threading;
using System.Threading.Tasks;

namespace Paykan.Events.Abstraction
{
    /// <summary>
    /// Publishes a event to its listener
    /// </summary>
    public interface IEventMediator
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
    }
}