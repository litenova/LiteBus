using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Events.Abstractions;

/// <summary>
/// Represents the mediator interface for publishing events within the application.
/// </summary>
/// <remarks>
/// The event mediator is responsible for broadcasting events to all registered handlers
/// and orchestrating the event handling pipeline. Unlike commands, which are handled by
/// exactly one handler, events can be handled by multiple handlers, allowing for decoupled
/// communication between different parts of the application.
/// 
/// In the publish-subscribe pattern, events represent notifications about something that
/// has happened in the system. The event mediator helps maintain separation between the
/// event publishers and the event subscribers (handlers).
/// </remarks>
public interface IEventMediator
{
    /// <summary>
    /// Asynchronously publishes an event.
    /// </summary>
    /// <param name="event">The event to be published.</param>
    /// <param name="eventMediationSettings">Optional settings for event mediation that control aspects such as handler filtering and error handling behavior.</param>
    /// <param name="cancellationToken">Cancellation token for the operation that can be used to cancel the event processing.</param>
    /// <returns>A task representing the asynchronous event publication operation.</returns>
    /// <remarks>
    /// This method broadcasts the event to all registered handlers for the event's type.
    /// The event handling pipeline is executed for each handler, including pre-handlers,
    /// the main handler, post-handlers, and error handlers if exceptions occur.
    /// 
    /// By default, if no handlers are found for the event, the operation completes successfully
    /// without any action. This behavior can be changed using the <see cref="EventMediationSettings"/>.
    /// </remarks>
    Task PublishAsync(IEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously publishes an event with a specific type.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to be published.</typeparam>
    /// <param name="event">The event to be published.</param>
    /// <param name="eventMediationSettings">Optional settings for event mediation that control aspects such as handler filtering and error handling behavior.</param>
    /// <param name="cancellationToken">Cancellation token for the operation that can be used to cancel the event processing.</param>
    /// <returns>A task representing the asynchronous event publication operation.</returns>
    /// <remarks>
    /// This method provides a strongly-typed alternative to the non-generic <see cref="PublishAsync(IEvent, EventMediationSettings?, CancellationToken)"/> method.
    /// It broadcasts the event to all registered handlers for the event's type.
    /// The event handling pipeline is executed for each handler, including pre-handlers,
    /// the main handler, post-handlers, and error handlers if exceptions occur.
    /// 
    /// By default, if no handlers are found for the event, the operation completes successfully
    /// without any action. This behavior can be changed using the <see cref="EventMediationSettings"/>.
    /// </remarks>
    Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default)
        where TEvent : notnull;
}