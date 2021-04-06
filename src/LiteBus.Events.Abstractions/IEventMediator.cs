using System.Threading.Tasks;

namespace LiteBus.Events.Abstractions
{
    /// <summary>
    ///     Publishes an event to its handlers
    /// </summary>
    public interface IEventMediator
    {
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
    }
}