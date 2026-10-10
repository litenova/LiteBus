using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The idempotency key a pipeline stage resolved for the message in flight.
/// </summary>
/// <param name="Value">The key, which must be stable across deliveries of the same intent.</param>
/// <remarks>
///     <para>
///         Some keys cannot be projected from the message. A processor webhook carries a raw body and a signature and
///         nothing else, deliberately, so that nothing unverified reaches a decision; its deduplication key is the
///         provider's event id, which is only knowable after the signature is checked and the body parsed. That is a
///         lookup, and a lookup needs services.
///     </para>
///     <para>
///         A guard is where that work belongs, and the pre stage runs guards before shortcuts, so a guard can verify
///         the message and hand the key forward through <see cref="IExecutionContext.Data" /> before the idempotency
///         shortcut asks for it:
///     </para>
///     <code><![CDATA[
/// context.Data.Set(new SuppliedIdempotencyKey(verified.EventId));
/// ]]></code>
///     <para>
///         The key is read once and reused by both the shortcut that claims it and the completion handler that
///         resolves it, so the expensive verification happens once per mediation rather than once per stage. Declare
///         the message with <see cref="IdempotencyDeclaration.KeyedByContext" /> to require this.
///     </para>
/// </remarks>
public sealed record SuppliedIdempotencyKey(string Value)
{
    /// <summary>
    ///     Gets the key, which must be stable across deliveries of the same intent.
    /// </summary>
    public string Value { get; } = string.IsNullOrWhiteSpace(Value)
        ? throw new ArgumentException("The supplied idempotency key must not be blank.", nameof(Value))
        : Value;
}
