using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Idempotency;

/// <summary>
///     Turns a message into the scoped key its idempotency declaration asks for.
/// </summary>
/// <remarks>
///     The seam the shipped shortcut and the shipped completion handler share, so both derive the same key for one
///     mediation without passing state between stages. The selector is a pure projection over the message, so
///     recomputing it is cheaper and safer than remembering it.
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
    /// <returns>The declaration and its resolved key, or <see langword="null" /> when the message declares none.</returns>
    /// <exception cref="LiteBusConfigurationException">The selector produced a blank key.</exception>
    /// <remarks>
    ///     A blank key is reported rather than used. Every message with a blank key collides with every other one, so
    ///     the first would answer all the rest, which is a data-loss bug wearing the costume of a working feature.
    /// </remarks>
    public ResolvedIdempotencyKey? Resolve(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        if (!_metadata.TryGet<IdempotencyDeclaration>(messageType, out var declaration))
        {
            return null;
        }

        var key = declaration.KeySelector(message);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new LiteBusConfigurationException(
                $"The idempotency declaration for '{messageType.Name}' produced a blank key. Every message with a "
                + "blank key shares one key space, so the first would answer all the others. Project the key from a "
                + "field the message always carries.");
        }

        return new ResolvedIdempotencyKey($"{declaration.Scope ?? messageType.Name}:{key}", declaration);
    }
}
