using System;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <inheritdoc cref="IMessageMetadataAccessor" />
/// <remarks>
///     A thin read-only view over the registry. It holds no state of its own, because metadata is resolved once when a
///     message type is registered and never changes afterwards, so a singleton is correct and there is nothing to
///     invalidate.
/// </remarks>
internal sealed class MessageMetadataAccessor : IMessageMetadataAccessor
{
    /// <summary>
    ///     The registry holding the descriptor of every registered message type.
    /// </summary>
    private readonly IMessageReader _reader;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageMetadataAccessor" /> class.
    /// </summary>
    /// <param name="reader">The registry holding the descriptor of every registered message type.</param>
    public MessageMetadataAccessor(IMessageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Metadata follows the descriptor the mediation resolved, not the registration scan. An unregistered runtime
    ///     type that the default resolve strategy mediates through a base descriptor reads that descriptor's metadata,
    ///     exactly as if the type had been registered and inherited the base declarations. Only a type nothing
    ///     registered covers raises <see cref="MessageMetadataNotFoundException" />; an ambiguous fallback raises
    ///     <see cref="AmbiguousMessageResolveException" />, the same exception mediating the type would raise.
    /// </remarks>
    public IMessageMetadata ForMessage(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return ResolveDescriptor(messageType)?.Metadata
               ?? throw new MessageMetadataNotFoundException(messageType);
    }

    /// <inheritdoc />
    public IMessageMetadata ForMessage<TMessage>()
        where TMessage : notnull
    {
        return ForMessage(typeof(TMessage));
    }

    /// <inheritdoc />
    public bool TryGet<TValue>(Type messageType, [MaybeNullWhen(false)] out TValue value)
        where TValue : notnull
    {
        return ForMessage(messageType).TryGet(out value);
    }

    /// <inheritdoc />
    public bool TryGet<TMessage, TValue>([MaybeNullWhen(false)] out TValue value)
        where TMessage : notnull
        where TValue : notnull
    {
        return ForMessage(typeof(TMessage)).TryGet(out value);
    }

    /// <summary>
    ///     Resolves the descriptor whose metadata governs a message type: the exact registration, or the
    ///     most-derived assignable one the default resolve strategy would mediate the type through.
    /// </summary>
    /// <param name="messageType">The message type to read.</param>
    /// <returns>The governing descriptor, or <see langword="null" /> when nothing registered covers the type.</returns>
    private IMessageDescriptor? ResolveDescriptor(Type messageType)
    {
        return new ActualTypeOrFirstAssignableTypeMessageResolveStrategy().Find(messageType, _reader);
    }
}
