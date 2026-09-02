using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Idempotency;

/// <summary>
///     One message's idempotency key and the declaration it came from.
/// </summary>
/// <param name="Key">The scoped key, prefixed by the declaration's scope or the message type name.</param>
/// <param name="Declaration">The declaration that produced the key, carrying the replay decision.</param>
public readonly record struct ResolvedIdempotencyKey(string Key, IdempotencyDeclaration Declaration);
