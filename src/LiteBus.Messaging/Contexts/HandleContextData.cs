using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Contexts;

/// <summary>
///     Provides a default implementation of the <see cref="IHandleContextData" /> interface.
/// </summary>
/// <remarks>
///     This class uses a dictionary to store context data, with the type as the key and the value as the value.
/// </remarks>
public class HandleContextData : IHandleContextData
{
    /// <summary>
    ///     Stores context values keyed by their CLR type.
    /// </summary>
    private readonly Dictionary<Type, object> _data = new();

    /// <inheritdoc />
    public T Get<T>()
    {
        return (T)_data[typeof(T)];
    }

    /// <inheritdoc />
    public void Set<T>(T value) where T : notnull
    {
        _data[typeof(T)] = value;
    }

    /// <inheritdoc />
    public bool Contains<T>()
    {
        return _data.ContainsKey(typeof(T));
    }

    /// <inheritdoc />
    public void Remove<T>()
    {
        _data.Remove(typeof(T));
    }
}
