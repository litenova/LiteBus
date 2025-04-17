using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a handler that processes events of type <typeparamref name="TEvent" />.
/// </summary>
/// <typeparam name="TEvent">The type of event to handle.</typeparam>
/// <remarks>
///     Event handlers implement the business logic that should execute in response to a specific event.
///     Unlike command handlers, multiple event handlers can subscribe to the same event type, allowing
///     for various parts of the system to react independently to the same event. Event handlers typically
///     execute asynchronously and can perform operations such as updating projections, notifying external
///     systems, or triggering additional workflows.
/// </remarks>
public interface IEventHandler<in TEvent> : IAsyncMessageHandler<TEvent>, IRegistrableEventConstruct where TEvent : notnull;