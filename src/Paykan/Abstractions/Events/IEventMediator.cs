using System.Threading;
using System.Threading.Tasks;

namespace Paykan.Abstractions
{
    public interface IEventMediator
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent;
    }
}