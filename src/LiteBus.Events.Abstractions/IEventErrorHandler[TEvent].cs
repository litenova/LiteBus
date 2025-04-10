using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during the processing of a specific
///     event type <typeparamref name="TEvent"/>.
/// </summary>
/// <typeparam name="TEvent">The specific event type this error handler targets.</typeparam>
/// <remarks>
///     Event type-specific error handlers provide targeted exception handling for particular event types.
///     They execute when an exception occurs during the processing of the specified event type.
///     This allows for implementing specialized error handling strategies for different event types,
///     such as custom recovery logic, retry policies, or specific error reporting for critical events.
/// </remarks>
public interface IEventErrorHandler<in TEvent> : IAsyncMessageErrorHandler<TEvent, object>, IRegistrableEventConstruct;