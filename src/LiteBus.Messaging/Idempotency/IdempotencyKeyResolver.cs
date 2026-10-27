using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Idempotency;

/// <summary>
///     Turns a message into the scoped key its idempotency declaration asks for.
/// </summary>
/// <remarks>
///     The seam the shipped shortcut and the shipped completion handler share, so both derive the same key for one
///     mediation without passing state between stages. A projected key is a pure function of the message, so
///     recomputing it is cheaper and safer than remembering it. A key that needed services to derive is computed once
///     by an earlier stage and read back from the execution context, which keeps the two stages agreeing without
///     paying for the derivation twice.
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
    ///     <see cref="IdempotencyDeclaration.KeyedByContext" />. Defaults to the ambient context of the mediation in
    ///     flight, which is what every shipped stage relies on; pass one explicitly only in a test.
    /// </param>
    /// <returns>
    ///     The declaration with its resolved key and fingerprint, or <see langword="null" /> when the message declares
    ///     none. The fingerprint is <see langword="null" /> when the declaration selects none.
    /// </returns>
    /// <exception cref="IdempotencyConfigurationException">
    ///     The selector produced a blank key, or the message declares a context-supplied key and no stage supplied one.
    /// </exception>
    /// <remarks>
    ///     A blank key is reported rather than used. Every message with a blank key collides with every other one, so
    ///     the first would answer all the rest, which is a data-loss bug wearing the costume of a working feature.
    /// </remarks>
    public ResolvedIdempotencyKey? Resolve(object message, IExecutionContext? executionContext = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        if (!_metadata.TryGet<IdempotencyDeclaration>(messageType, out var declaration))
        {
            return null;
        }

        var key = declaration.KeySelector is null
            ? ResolveSuppliedKey(messageType, executionContext ?? AmbientExecutionContext.GetCurrentOrDefault())
            : declaration.KeySelector(message);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new IdempotencyConfigurationException(
                $"The idempotency declaration for '{messageType.Name}' produced a blank key. Every message with a "
                + "blank key shares one key space, so the first would answer all the others. Project the key from a "
                + "field the message always carries.");
        }

        var fingerprint = declaration.FingerprintSelector?.Invoke(message);

        return new ResolvedIdempotencyKey($"{declaration.Scope ?? messageType.Name}:{key}", declaration, fingerprint);
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
}
