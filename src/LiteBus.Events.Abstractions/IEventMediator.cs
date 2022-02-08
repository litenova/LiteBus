using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Publishes an event to its handlers
/// </summary>
public interface IEventMediator : IEventConstruct
{
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);

    void Publish(IEvent @event);
}