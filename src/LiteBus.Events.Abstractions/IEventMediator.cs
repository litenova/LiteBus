using System.Threading;
using MorseCode.ITask;

namespace LiteBus.Events.Abstractions
{
    /// <summary>
    ///     Publishes an event to its handlers
    /// </summary>
    public interface IEventMediator
    {
        ITask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
    }
}