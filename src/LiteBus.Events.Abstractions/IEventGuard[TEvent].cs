using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether an event is permitted to reach its handlers.
/// </summary>
/// <typeparam name="TEvent">The specific event type this guard runs for.</typeparam>
/// <remarks>
///     An event is a fact that already happened, so denying one is rare and usually wrong. Skipping the reactions to
///     an event this process has already handled is the useful case, and it belongs to
///     <see cref="IEventShortcut{TEvent}" />. To select handlers rather than stop the broadcast, use handler filtering.
/// </remarks>
public interface IEventGuard<in TEvent> : IMessageGuard<TEvent>
    where TEvent : IEvent;
