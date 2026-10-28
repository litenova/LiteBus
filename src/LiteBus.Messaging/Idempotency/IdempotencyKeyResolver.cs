using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Idempotency;

/// <summary>
///     Turns a message into the scoped key its idempotency declaration asks for.
/// </summary>
/// <remarks>
///     The seam the shipped shortcuts share, so every arity derives the same key for one mediation. A projected key is
///     a pure function of the message. A key that needed services to derive is computed once by an earlier stage and
///     read back from the execution context, which keeps the derivation cost at once per mediation. The shortcut
///     records the granted claim, and the completion handler settles from that record without resolving again, so a
///     handler that mutates the message cannot turn a success into a resolution failure.
/// </remarks>
public sealed class IdempotencyKeyResolver
{
    /// <summary>
    ///     Reads the declaration from the message type's metadata.
    /// </summary>
    private readonly IMessageMetadataAccessor _metadata;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyKeyResolver" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type's metadata.</param>
    public IdempotencyKeyResolver(IMessageMetadataAccessor metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
    }

    /// <summary>
    ///     Resolves the scoped key for a message, when the message declares idempotency at all.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="executionContext">
    ///     The execution context of the mediation in flight, which carries the key for a message declared with
    ///     <see cref="IdempotencyDeclaration.KeyedByContext" /> and the tenant the key is scoped to. Defaults to the
    ///     ambient context of the mediation in flight, which is what every shipped stage relies on; pass one explicitly
    ///     only in a test.
    /// </param>
    /// <returns>
    ///     The declaration with its resolved key and fingerprint, or <see langword="null" /> when the message declares
    ///     none. The fingerprint is <see langword="null" /> when the declaration selects none.
    /// </returns>
    /// <exception cref="IdempotencyConfigurationException">
    ///     The selector produced a blank key, the declaration sets a blank scope, or the message declares a
    ///     context-supplied key and no stage supplied one.
    /// </exception>
    /// <remarks>
    ///     A blank key is reported rather than used. Every message with a blank key collides with every other one, so
    ///     the first would answer all the rest, which is a data-loss bug wearing the costume of a working feature. A
    ///     blank explicit scope is reported for the same reason; <see langword="null" /> keeps meaning the default.
    /// </remarks>
    public ResolvedIdempotencyKey? Resolve(object message, IExecutionContext? executionContext = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        if (!_metadata.TryGet<IdempotencyDeclaration>(messageType, out var declaration))
        {
            return null;
        }

        if (declaration.Scope is not null && string.IsNullOrWhiteSpace(declaration.Scope))
        {
            throw new IdempotencyConfigurationException(
                $"The idempotency declaration for '{messageType.Name}' sets a blank scope. Every message with a "
                + "blank scope shares one key space, so the first would answer all the others. Leave the scope "
                + "unset to scope by the message type, or set a scope that names the key space the message joins.");
        }

        var context = executionContext ?? AmbientExecutionContext.GetCurrentOrDefault();

        var key = declaration.KeySelector is null
            ? ResolveSuppliedKey(messageType, context)
            : declaration.KeySelector(message);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new IdempotencyConfigurationException(
                $"The idempotency declaration for '{messageType.Name}' produced a blank key. Every message with a "
                + "blank key shares one key space, so the first would answer all the others. Project the key from a "
                + "field the message always carries.");
        }

        var fingerprint = declaration.FingerprintSelector?.Invoke(message);
        var scope = declaration.Scope ?? messageType.FullName ?? messageType.Name;
        var tenant = ReadTenant(context);

        var storeKey = tenant is null
            ? $"{EscapeKeySegment(scope)}:{EscapeKeySegment(key)}"
            : $"{EscapeKeySegment(tenant)}:{EscapeKeySegment(scope)}:{EscapeKeySegment(key)}";

        return new ResolvedIdempotencyKey(storeKey, declaration, fingerprint);
    }

    /// <summary>
    ///     Reads the key an earlier pipeline stage stored for a message declared with
    ///     <see cref="IdempotencyDeclaration.KeyedByContext" />.
    /// </summary>
    /// <param name="messageType">The message type being mediated, used to name the failure.</param>
    /// <param name="executionContext">The execution context of the mediation in flight.</param>
    /// <returns>The supplied key.</returns>
    /// <exception cref="IdempotencyConfigurationException">No stage supplied a key.</exception>
    /// <remarks>
    ///     Reported rather than defaulted. A generated fallback would make every delivery of the same intent look
    ///     distinct, so the store would accept them all and the declaration would appear to work while doing nothing.
    /// </remarks>
    private static string ResolveSuppliedKey(Type messageType, IExecutionContext? executionContext)
    {
        if (executionContext is not null &&
            executionContext.Data.TryGet<SuppliedIdempotencyKey>(out var supplied))
        {
            return supplied.Value;
        }

        throw new IdempotencyConfigurationException(
            $"The idempotency declaration for '{messageType.Name}' expects its key from the execution context, but "
            + "no pipeline stage supplied one. Set it from a guard, which runs before the idempotency shortcut, with "
            + $"context.Data.Set(new {nameof(SuppliedIdempotencyKey)}(key)).");
    }

    /// <summary>
    ///     Reads the tenant the resolved key is scoped to from the mediation in flight.
    /// </summary>
    /// <param name="executionContext">The execution context of the mediation in flight.</param>
    /// <returns>The tenant, or <see langword="null" /> when the mediation carries none.</returns>
    /// <remarks>
    ///     The tenant arrives the way trace metadata does: inbox dispatchers copy the stored envelope tenant into the
    ///     mediation items, and an application mediation sets the same entry from its own caller context. Without it,
    ///     two tenants using one key string for one command would share a store row, and one tenant's repeat would be
    ///     answered with the other's stored result.
    /// </remarks>
    private static string? ReadTenant(IExecutionContext? executionContext)
    {
        if (executionContext is not null &&
            executionContext.Items.TryGetValue(MessageTraceContextKeys.TenantId, out var value) &&
            value is string tenant &&
            !string.IsNullOrWhiteSpace(tenant))
        {
            return tenant;
        }

        return null;
    }

    /// <summary>
    ///     Escapes one resolved-key segment so the joined key cannot collide across distinct inputs.
    /// </summary>
    /// <param name="value">The tenant, scope, or key segment to escape.</param>
    /// <returns>The segment with backslashes doubled and separators escaped.</returns>
    /// <remarks>
    ///     The join is unambiguous because an encoded segment never carries a raw separator: every source backslash is
    ///     doubled first, so a backslash followed by anything else can only be an escape. Two segments joined by one
    ///     separator therefore decode to exactly one segment pair, and a tenant-prefixed key carries two separators
    ///     where an unprefixed one carries one, so no two distinct tenant, scope, and key tuples produce one string.
    /// </remarks>
    private static string EscapeKeySegment(string value)
    {
        return value.Replace("\\", "\\\\").Replace(":", "\\:");
    }
}
