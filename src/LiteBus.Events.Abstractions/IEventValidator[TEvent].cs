using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Decides whether an event of type <typeparamref name="TEvent" /> is well-formed.
/// </summary>
/// <typeparam name="TEvent">The specific event type this validator runs for.</typeparam>
/// <remarks>
///     A validator returns <see cref="Validity" /> rather than throwing, so a malformed event reports
///     <see cref="MediationOutcome.Invalid" /> instead of arriving at error handlers as a fault. An event produces no
///     result, so a validation failure always reaches the publisher as
///     <see cref="LiteBusMessageInvalidException" />.
/// </remarks>
public interface IEventValidator<in TEvent> : IMessageValidator<TEvent>
    where TEvent : IEvent;
