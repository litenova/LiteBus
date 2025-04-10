using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during any event processing.
/// </summary>
/// <remarks>
///     Event error handlers provide centralized exception handling for the event pipeline.
///     They execute when any exception occurs during event processing (in pre-handlers, handlers, or post-handlers).
///     Multiple error handlers can be registered to implement different error handling strategies such as
///     logging, retries, or custom recovery logic for all events.
/// </remarks>
public interface IEventErrorHandler : IRegistrableEventConstruct, IAsyncMessageErrorHandler<IEvent, object>;