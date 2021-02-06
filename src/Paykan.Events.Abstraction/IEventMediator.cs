using System.Threading;
using System.Threading.Tasks;

namespace Paykan.Events.Abstraction
{
    /// <summary>
    ///     Publishes an event to its handlers
    /// </summary>
    public interface IEventMediator
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
    }
}