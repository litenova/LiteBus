using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that executes before a specific event type <typeparamref name="TEvent"/> is processed.
/// </summary>
/// <typeparam name="TEvent">The specific event type this pre-handler targets.</typeparam>
/// <remarks>
///     Type-specific event pre-handlers run before the event handlers for the specified event type.
///     They can be used for event-specific validation, enrichment, or other cross-cutting concerns
///     that apply only to the specified event type. Multiple type-specific pre-handlers can be
///     registered for each event type and will execute in sequence before the actual event handlers.
/// </remarks>
public interface IEventPreHandler<in TEvent> : IAsyncMessagePreHandler<TEvent>, IRegistrableEventConstruct where TEvent : notnull;