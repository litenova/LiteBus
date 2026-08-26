using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a handler that executes when mediation of <typeparamref name="TEvent" /> ends, whatever the outcome.
/// </summary>
/// <typeparam name="TEvent">The specific event type this completion handler observes.</typeparam>
public interface IEventCompletionHandler<TEvent> : IMessageCompletionHandler<TEvent>
    where TEvent : IEvent;
