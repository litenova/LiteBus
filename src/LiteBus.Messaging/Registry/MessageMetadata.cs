using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Type-keyed metadata collected for one message type during registration.
/// </summary>
internal sealed class MessageMetadata : IMessageMetadata
{
    /// <summary>
    ///     Metadata values keyed by the CLR type they were stored under.
    /// </summary>
    private readonly Dictionary<Type, object> _values = new();

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
    ///     Stores a metadata value under an explicit key type, replacing any existing value of that type.
    /// </summary>
    /// <param name="keyType">The CLR type used as the metadata key.</param>
    /// <param name="value">The value to store.</param>
    public void Set(Type keyType, object value)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(value);
        _values[keyType] = value;
    }
}
