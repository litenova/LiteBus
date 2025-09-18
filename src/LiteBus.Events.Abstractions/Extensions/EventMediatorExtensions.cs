using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Provides extension methods for <see cref="IEventMediator" /> for simplified event publishing.
/// </summary>
public static class EventMediatorExtensions
{
    /// <summary>
    ///     Publishes an event asynchronously using the specified event mediator.
    /// </summary>
    /// <param name="eventMediator">The event mediator to use for publishing the event.</param>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the publish operation.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    /// <example>
    ///     Usage example:
    ///     <code>
    /// await eventMediator.PublishAsync(myEvent, cancellationToken);
    /// </code>
    /// </example>
    public static Task PublishAsync(this IEventMediator eventMediator, IEvent @event, CancellationToken cancellationToken = default)
    {
        return eventMediator.PublishAsync(@event, null, cancellationToken);
    }

    /// <summary>
    ///     Publishes an event asynchronously using the specified event mediator with a specific tag.
    /// </summary>
    /// <param name="eventMediator">The event mediator to use for publishing the event.</param>
    /// <param name="event">The event to publish.</param>
    /// <param name="tag">A tag that specifies the context or category of the event.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the publish operation.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    /// <example>
    ///     Usage example:
    ///     <code>
    /// await eventMediator.PublishAsync(myEvent, "UserAction", cancellationToken);
    /// </code>
    /// </example>
    public static Task PublishAsync(this IEventMediator eventMediator, IEvent @event, string tag, CancellationToken cancellationToken = default)
    {
        return eventMediator.PublishAsync(@event,
            new EventMediationSettings
            {
                Routing =
                {
                    Tags = [tag]
                }
            },
            cancellationToken);
    }
}