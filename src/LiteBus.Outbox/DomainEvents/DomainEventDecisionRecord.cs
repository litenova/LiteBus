using System;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     One domain event type and the decision the staging handler applies to it.
/// </summary>
/// <param name="EventType">The closed domain event type the decision applies to.</param>
/// <param name="Kind">What the staging handler does with the event type.</param>
public sealed record DomainEventDecisionRecord(Type EventType, DomainEventDecisionKind Kind);
