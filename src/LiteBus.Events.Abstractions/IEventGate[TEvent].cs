using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether an event reaches its handlers.
/// </summary>
/// <typeparam name="TEvent">The specific event type this gate runs for.</typeparam>
/// <remarks>
///     An event is a fact that already happened, so refusing one is rare and usually wrong. The useful case is
///     <see cref="PipelineDirective.ShortCircuit" />, which skips the reactions to an event this process has already
///     handled, and reports <see cref="MessageOutcome.ShortCircuited" />.
/// </remarks>
public interface IEventGate<in TEvent> : IMessageGate<TEvent>
    where TEvent : IEvent;
