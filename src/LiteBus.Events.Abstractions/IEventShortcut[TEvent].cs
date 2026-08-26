using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that skips the reactions to an event this process has already handled.
/// </summary>
/// <typeparam name="TEvent">The specific event type this shortcut runs for.</typeparam>
/// <remarks>
///     Return <see cref="Shortcut.Skip" /> when the event has already been processed, which the mediation reports as
///     <see cref="MessageOutcome.ShortCircuited" /> and an audit trail records as a success. This is the useful shape
///     on the event axis, because an event is a fact and refusing a fact is rarely meaningful.
/// </remarks>
public interface IEventShortcut<in TEvent> : IMessageShortcut<TEvent>
    where TEvent : IEvent;
