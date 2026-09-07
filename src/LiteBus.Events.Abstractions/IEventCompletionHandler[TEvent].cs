using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a handler that executes when mediation of <typeparamref name="TEvent" /> ends, whatever the outcome.
/// </summary>
/// <typeparam name="TEvent">The specific event type this completion handler observes.</typeparam>
/// <remarks>
///     The constraint is <see langword="notnull" /> rather than <see cref="IEvent" /> so that a domain
///     event which deliberately carries no LiteBus marker can use the whole pipeline, not only
///     <see cref="IEventHandler{TEvent}" />. Every role on this axis agrees on that constraint.
/// </remarks>
public interface IEventCompletionHandler<TEvent> : IMessageCompletionHandler<TEvent>
    where TEvent : notnull;
