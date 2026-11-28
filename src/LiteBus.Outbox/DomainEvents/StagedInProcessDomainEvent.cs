using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     One domain event recorded for publication in process after the unit of work commits.
/// </summary>
/// <param name="DomainEvent">The harvested or collected domain event to publish.</param>
/// <param name="Cause">The committed fact the published mediation carries as its cause.</param>
/// <param name="TenantId">The tenant the causing mediation ran in, or <see langword="null" /> when the work belongs to no tenant.</param>
/// <param name="TraceContext">The distributed trace context of the causing mediation, or <see langword="null" /> when it carries none.</param>
internal sealed record StagedInProcessDomainEvent(
    object DomainEvent,
    MediationCause Cause,
    string? TenantId,
    string? TraceContext);
