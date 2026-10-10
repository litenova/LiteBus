using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Type-keyed metadata collected for one message type during registration.
/// </summary>
/// <remarks>
///     Two sources declare metadata, and both may cover the same message: an attribute on the message type, and a
///     definition declared for the message type or for something it derives from. Provenance is tracked alongside each
///     value so that precedence is a stated rule rather than an accident of registration order.
/// </remarks>
internal sealed class MessageMetadata : IMessageMetadata
{
    /// <summary>
    ///     The message type this metadata belongs to, used in configuration diagnostics.
    /// </summary>
    private readonly Type _messageType;

    /// <summary>
    ///     The message type and source kind that supplied each stored value, keyed by metadata key type.
    /// </summary>
    private readonly Dictionary<Type, (Type DeclaringMessageType, MetadataSourceKind Kind)> _sources = new();

    /// <summary>
    ///     Metadata values keyed by the CLR type they were stored under.
    /// </summary>
    private readonly Dictionary<Type, object> _values = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageMetadata" /> class.
    /// </summary>
    /// <param name="messageType">The message type this metadata belongs to.</param>
    public MessageMetadata(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        _messageType = messageType;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<object> Values => _values.Values;

    /// <inheritdoc />
    public bool TryGet<TValue>([MaybeNullWhen(false)] out TValue value)
        where TValue : notnull
    {
        if (_values.TryGetValue(typeof(TValue), out var stored) && stored is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public bool Contains<TValue>()
        where TValue : notnull
    {
        return _values.ContainsKey(typeof(TValue));
    }

    /// <summary>
    ///     Stores a metadata value, keeping the one declared closest to this message type.
    /// </summary>
    /// <param name="keyType">The CLR type used as the metadata key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="declaringMessageType">The message type the declaration was written for.</param>
    /// <param name="kind">Where the declaration came from.</param>
    /// <exception cref="MessageDeclarationException">
    ///     Thrown when the value does not match its key type, or when two declarations cover this message and neither is
    ///     more derived than the other.
    /// </exception>
    /// <remarks>
    ///     A declaration written for the message itself beats one written for a base type or interface, and a definition
    ///     beats an attribute on the same message type. Anything else is ambiguous and is reported rather than resolved
    ///     by registration order.
    /// </remarks>
    public void Set(Type keyType, object value, Type declaringMessageType, MetadataSourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(declaringMessageType);

        if (!keyType.IsInstanceOfType(value))
        {
            throw new MessageDeclarationException(
                $"A declaration for '{_messageType.Name}' produced a value of type '{value.GetType().Name}', "
                + $"but declared its metadata key as '{keyType.Name}'.");
        }

        if (_sources.TryGetValue(keyType, out var existing)
            && !Wins(declaringMessageType, kind, existing.DeclaringMessageType, existing.Kind))
        {
            return;
        }

        _values[keyType] = value;
        _sources[keyType] = (declaringMessageType, kind);
    }

    /// <summary>
    ///     Determines whether an incoming declaration replaces the one already stored.
    /// </summary>
    /// <param name="incomingMessageType">The message type the incoming declaration was written for.</param>
    /// <param name="incomingKind">Where the incoming declaration came from.</param>
    /// <param name="storedMessageType">The message type the stored declaration was written for.</param>
    /// <param name="storedKind">Where the stored declaration came from.</param>
    /// <returns><see langword="true" /> when the incoming declaration is the more specific one.</returns>
    /// <exception cref="MessageDeclarationException">
    ///     Thrown when neither declaration is more derived than the other.
    /// </exception>
    private bool Wins(
        Type incomingMessageType,
        MetadataSourceKind incomingKind,
        Type storedMessageType,
        MetadataSourceKind storedKind)
    {
        if (incomingMessageType == storedMessageType)
        {
            return incomingKind > storedKind;
        }

        if (storedMessageType.IsAssignableFrom(incomingMessageType))
        {
            return true;
        }

        if (incomingMessageType.IsAssignableFrom(storedMessageType))
        {
            return false;
        }

        throw new MessageDeclarationException(
            $"The message '{_messageType.Name}' is covered by two declarations of the same kind, one for "
            + $"'{storedMessageType.Name}' and one for '{incomingMessageType.Name}', and neither is more derived than "
            + "the other. Declare the value for the message itself to say which one applies.");
    }
}
