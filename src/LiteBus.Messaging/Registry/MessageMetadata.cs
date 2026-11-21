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
///     Four sources declare metadata, and several may cover the same message: an attribute on the message type, a
///     definition declared for the message type or for something it derives from, a family default stated in
///     composition code, and a convention derived from the message type. Provenance is tracked alongside each
///     value so that precedence is a stated rule rather than an accident of registration order.
/// </remarks>
internal sealed class MessageMetadata : IMessageMetadata
{
    /// <summary>
    ///     The message type this metadata belongs to, used in configuration diagnostics.
    /// </summary>
    private readonly Type _messageType;

    /// <summary>
    ///     The declaring message type, source kind, and definition or attribute type that supplied each stored value,
    ///     keyed by metadata key type.
    /// </summary>
    private readonly Dictionary<Type, (Type DeclaringMessageType, MetadataSourceKind Kind, Type? SourceType)> _sources = new();

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

    /// <summary>
    ///     Gets one provenance entry per effective value, as the composition model reports it.
    /// </summary>
    /// <value>The effective values with the source that supplied each.</value>
    internal IReadOnlyList<MessageDeclarationProvenance> Provenance
    {
        get
        {
            List<MessageDeclarationProvenance> provenance = [];

            foreach (var pair in _values)
            {
                var source = _sources[pair.Key];

                provenance.Add(new MessageDeclarationProvenance(
                    pair.Key,
                    pair.Value,
                    MapSourceKind(source.Kind),
                    source.DeclaringMessageType,
                    source.SourceType));
            }

            return provenance;
        }
    }

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
    /// <param name="sourceType">
    ///     The definition class or attribute type that declared the value, or <see langword="null" /> when the source
    ///     is a composition default, a convention, or an aggregated exemption set rather than one type.
    /// </param>
    /// <exception cref="MessageDeclarationException">
    ///     Thrown when the value does not match its key type, or when two declarations cover this message and neither is
    ///     more derived than the other.
    /// </exception>
    /// <remarks>
    ///     A declaration written for the message itself beats one written for a base type or interface, and a definition
    ///     beats an attribute on the same message type. Anything else is ambiguous and is reported rather than resolved
    ///     by registration order.
    /// </remarks>
    public void Set(Type keyType, object value, Type declaringMessageType, MetadataSourceKind kind, Type? sourceType = null)
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
        _sources[keyType] = (declaringMessageType, kind, sourceType);
    }

    /// <summary>
    ///     Stores a convention value when no other source declared the key.
    /// </summary>
    /// <param name="keyType">The CLR type used as the metadata key.</param>
    /// <param name="value">The convention value to store.</param>
    /// <param name="messageType">The concrete message type the convention was evaluated for.</param>
    /// <returns>
    ///     <see langword="true" /> when the value was stored; <see langword="false" /> when a value was already
    ///     present and the convention yielded to it.
    /// </returns>
    /// <exception cref="MessageDeclarationException">The value does not match its key type.</exception>
    /// <remarks>
    ///     Conventions apply after every other source, so presence alone decides: anything already stored, whether
    ///     from a definition, an attribute, or a default, wins without comparing specificity. The source is recorded
    ///     at the lowest rank, so a definition arriving later would still win; none does, because conventions apply in
    ///     a composition validation, after every module has registered.
    /// </remarks>
    public bool TryApplyConvention(Type keyType, object value, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(messageType);

        if (!keyType.IsInstanceOfType(value))
        {
            throw new MessageDeclarationException(
                $"A convention for '{messageType.Name}' produced a value of type '{value.GetType().Name}', "
                + $"but declared its metadata key as '{keyType.Name}'.");
        }

        if (_values.ContainsKey(keyType))
        {
            return false;
        }

        _values[keyType] = value;
        _sources[keyType] = (messageType, MetadataSourceKind.Convention, null);
        return true;
    }

    /// <summary>
    ///     Maps a registration source kind to the composition source kind the model reports.
    /// </summary>
    /// <param name="kind">The registration source kind stored with the value.</param>
    /// <returns>The composition source kind naming the same source.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind" /> names no known source.</exception>
    /// <remarks>
    ///     The two enums order their members differently, because the registration order encodes precedence while the
    ///     composition order is a reporting contract, so the mapping names each member rather than casting the ordinal.
    /// </remarks>
    private static DeclarationSourceKind MapSourceKind(MetadataSourceKind kind)
    {
        return kind switch
        {
            MetadataSourceKind.Convention => DeclarationSourceKind.Convention,
            MetadataSourceKind.Default => DeclarationSourceKind.Default,
            MetadataSourceKind.Attribute => DeclarationSourceKind.Attribute,
            MetadataSourceKind.Definition => DeclarationSourceKind.Definition,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown metadata source kind."),
        };
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
