namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an event in the publish-subscribe pattern, indicating that something significant has occurred in the system.
/// </summary>
/// <remarks>
///     Events are messages that notify interested parties about something that has happened in the system.
///     Unlike commands, events can have multiple subscribers (handlers), and they represent facts that have already occurred.
///     Events are typically used to decouple components and enable asynchronous processing across system boundaries.
/// </remarks>
public interface IEvent : IRegistrableEventConstruct;