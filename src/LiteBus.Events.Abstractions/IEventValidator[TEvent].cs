using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Decides whether an event of type <typeparamref name="TEvent" /> is well-formed.
/// </summary>
/// <typeparam name="TEvent">The specific event type this validator runs for.</typeparam>
/// <remarks>
///     The constraint is <see langword="notnull" /> rather than <see cref="IEvent" /> so that a domain
///     event which deliberately carries no LiteBus marker can use the whole pipeline, not only
///     <see cref="IEventHandler{TEvent}" />. Every role on this axis agrees on that constraint.
///     <para />
///     A validator returns <see cref="Validity" /> rather than throwing, so a malformed event reports
///     <see cref="MediationOutcome.Invalid" /> instead of arriving at error handlers as a fault. An event produces no
///     result, so a validation failure always reaches the publisher as
///     <see cref="LiteBusMessageInvalidException" />.
/// </remarks>
public interface IEventValidator<in TEvent> : IMessageValidator<TEvent>
    where TEvent : notnull;
