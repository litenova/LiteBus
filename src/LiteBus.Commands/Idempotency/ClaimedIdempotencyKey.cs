using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     The idempotency key the shortcut stage claimed for the mediation in flight.
/// </summary>
/// <param name="Value">The scoped key this mediation was granted.</param>
/// <remarks>
///     <para>
///         Recorded by the idempotency shortcut on <see cref="IdempotencyClaimOutcome.Granted" /> only, and read back by
///         <see cref="IdempotencyCompletionHandler" />. A mediation that never claimed, because a
///         guard denied it, a validator rejected it, the shortcut answered a repeat, or the claim threw a conflict,
///         holds no record, so the completion handler settles nothing and cannot delete another delivery's in-flight
///         claim.
///     </para>
///     <para>
///         One value per mediation is enough: exactly one idempotency shortcut applies to any given command, so a
///         mediation claims at most one key.
///     </para>
/// </remarks>
internal sealed record ClaimedIdempotencyKey(string Value);
