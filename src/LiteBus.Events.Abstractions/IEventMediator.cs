using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Events.Abstractions;

/// <summary>
/// Defines the interface for an event mediator that facilitates the publishing of events.
/// </summary>
public interface IEventMediator
{
    /// <summary>
    /// Publishes an event asynchronously.
    /// </summary>
    /// <param name="event">The event to publish.</param>
    /// <param name="settings">Optional settings for event mediation. If null, default settings are used.</param>
    /// <param name="cancellationToken">A token for cancelling the publish operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishAsync(IEvent @event, EventMediationSettings settings = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event asynchronously.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to publish.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="settings">Optional settings for event mediation. If null, default settings are used.</param>
    /// <param name="cancellationToken">A token for cancelling the publish operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings settings = null, CancellationToken cancellationToken = default);
}