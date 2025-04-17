using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a post-handler that executes after a specific event type <typeparamref name="TEvent" /> is processed.
/// </summary>
/// <typeparam name="TEvent">The specific event type this post-handler targets.</typeparam>
/// <remarks>
///     Type-specific event post-handlers run after all handlers for the specified event type have completed execution.
///     They can be used for event-specific logging, cleanup operations, or other cross-cutting concerns that apply
///     only to the specified event type. Multiple type-specific post-handlers can be registered for each event type
///     and will execute in sequence after the event handlers have processed the event.
/// </remarks>
public interface IEventPostHandler<in TEvent> : IAsyncMessagePostHandler<TEvent>, IRegistrableEventConstruct where TEvent : notnull;