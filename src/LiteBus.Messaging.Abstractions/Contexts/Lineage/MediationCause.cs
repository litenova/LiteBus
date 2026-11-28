namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The committed fact that caused a root mediation, carried as data rather than as an ambient scope.
/// </summary>
/// <param name="CorrelationId">The stable identifier of the workflow this mediation belongs to.</param>
/// <param name="CausationId">The identifier of the message, schedule, or record that caused this mediation.</param>
/// <param name="Source">Where the causing fact came from.</param>
public sealed record MediationCause(string CorrelationId, string CausationId, MediationCauseSource Source);
