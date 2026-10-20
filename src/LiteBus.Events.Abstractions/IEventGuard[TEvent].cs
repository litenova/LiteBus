using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether an event is permitted to reach its handlers.
/// </summary>
/// <typeparam name="TEvent">The specific event type this guard runs for.</typeparam>
/// <remarks>
///     The constraint is <see langword="notnull" /> rather than <see cref="IEvent" /> so that a domain
///     event which deliberately carries no LiteBus marker can use the whole pipeline, not only
///     <see cref="IEventHandler{TEvent}" />. Every role on this axis agrees on that constraint.
///     <para />
///     An event is a fact that already happened, so denying one is rare and usually wrong. Skipping the reactions to
///     an event this process has already handled is the useful case, and it belongs to
///     <see cref="IEventShortcut{TEvent}" />. To select handlers rather than stop the broadcast, use handler filtering.
/// </remarks>
public interface IEventGuard<in TEvent> : IMessageGuard<TEvent>
    where TEvent : notnull;
