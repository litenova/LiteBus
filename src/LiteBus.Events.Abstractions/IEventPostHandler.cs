using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a post-handler that executes after any event is processed.
/// </summary>
/// <remarks>
///     Event post-handlers run after all event handlers for an event have completed execution.
///     They can be used for cross-cutting concerns such as logging, cleanup operations, or monitoring
///     that should occur after every event is handled. Multiple post-handlers can be registered
///     in the application and they will all execute after each event is processed.
/// </remarks>
public interface IEventPostHandler : IAsyncMessagePostHandler<IEvent>, IRegistrableEventConstruct;