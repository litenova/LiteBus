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
    public IMessageMetadata ForMessage(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return _reader.Find(messageType)?.Metadata
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
}
