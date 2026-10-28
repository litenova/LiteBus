using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that skips the reactions to an event this process has already handled.
/// </summary>
/// <typeparam name="TEvent">The specific event type this shortcut runs for.</typeparam>
/// <remarks>
///     The constraint is <see langword="notnull" /> rather than <see cref="IEvent" /> so that a domain
///     event which deliberately carries no LiteBus marker can use the whole pipeline, not only
///     <see cref="IEventHandler{TEvent}" />. Every role on this axis agrees on that constraint.
///     <para />
///     Return <see cref="Shortcut.Answer" /> when the event has already been processed, which the mediation reports as
///     <see cref="MediationOutcome.Answered" /> and an audit trail records as a success. This is the useful shape
///     on the event axis, because an event is a fact and denying a fact is rarely meaningful.
/// </remarks>
public interface IEventShortcut<in TEvent> : IMessageShortcut<TEvent>
    where TEvent : notnull;
