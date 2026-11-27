using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Idempotency;

/// <summary>
///     One message's idempotency key and the declaration it came from.
/// </summary>
/// <param name="Key">
///     The scoped key: the execution identity tenant and a slash when the mediation carries one, then the
///     declaration's scope or the message full type name, then the projected key, with each segment escaped and the
///     scope and key joined by a colon.
/// </param>
/// <param name="Declaration">The declaration that produced the key, carrying the replay decision.</param>
/// <param name="Fingerprint">
///     The fingerprint the declaration selected from the message, or <see langword="null" /> when the declaration
///     selects none. The shortcut claims with it so the store can tell the same request from a different request
///     reusing the key.
/// </param>
public readonly record struct ResolvedIdempotencyKey(string Key, KeyedIdempotencyDeclaration Declaration, string? Fingerprint = null);
