#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Events.Abstractions;

/// <summary>
/// Represents the mediator interface for publishing events within the application.
/// </summary>
public interface IEventMediator
{
    /// <summary>
    /// Asynchronously publishes an event.
    /// </summary>
    /// <param name="event">The event to be published.</param>
    /// <param name="eventMediationSettings">Optional settings for event mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous event publication operation.</returns>
    Task PublishAsync(IEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously publishes an event with a specific type.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to be published.</typeparam>
    /// <param name="event">The event to be published.</param>
    /// <param name="eventMediationSettings">Optional settings for event mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous event publication operation.</returns>
    Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default) where TEvent : notnull;
}