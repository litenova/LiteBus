namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The idempotency key the in-flight command was granted.
/// </summary>
/// <param name="Value">The scoped key this mediation was granted.</param>
/// <remarks>
///     <para>
///         Recorded by the idempotency shortcut in <see cref="IExecutionContext.Data" /> when a claim is granted, and
///         read back when the mediation ends so exactly the claim this mediation holds is settled. A mediation that
///         never claimed, because a guard denied it, a validator rejected it, the shortcut answered a repeat, or the
///         claim threw a conflict, holds no record, so settling by the recorded claim cannot release another
///         delivery's in-flight key.
///     </para>
///     <para>
///         One value per mediation is enough: exactly one idempotency shortcut applies to any given command, so a
///         mediation claims at most one key. Handlers can also read the granted key through this record, which is how
///         a durable writer keys its envelope by the command's key.
///     </para>
/// </remarks>
public sealed record ClaimedIdempotencyKey(string Value);
