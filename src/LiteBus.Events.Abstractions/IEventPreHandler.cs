using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that executes before any event is processed.
/// </summary>
/// <remarks>
///     Event pre-handlers run before events are dispatched to their respective handlers.
///     They can be used for cross-cutting concerns such as logging, validation, enrichment,
///     or security checks that should be applied to all events. Multiple pre-handlers can be
///     registered in the application and they will all execute before each event is handled.
/// </remarks>
public interface IEventPreHandler : IAsyncMessagePreHandler<IEvent>, IRegistrableEventConstruct;