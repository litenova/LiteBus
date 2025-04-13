using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Provides a mechanism for storing and retrieving data within the context of a message handling operation.
/// </summary>
/// <remarks>
/// This interface allows handlers to share data with each other during the message handling process.
/// It provides type-safe access to context data through generic methods.
/// </remarks>
public interface IHandleContextData
{
    /// <summary>
    /// Gets a value of the specified type from the context.
    /// </summary>
    /// <typeparam name="T">The type of the value to get.</typeparam>
    /// <returns>The value of type <typeparamref name="T"/> stored in the context.</returns>
    /// <remarks>
    /// Throws an exception if no value of the specified type exists in the context.
    /// </remarks>
    T Get<T>();

    /// <summary>
    /// Sets a value of the specified type in the context.
    /// </summary>
    /// <typeparam name="T">The type of the value to set.</typeparam>
    /// <param name="value">The value to store in the context.</param>
    /// <remarks>
    /// If a value of the same type already exists in the context, it will be replaced.
    /// </remarks>
    void Set<T>(T value) where T : notnull;

    /// <summary>
    /// Determines whether the context contains a value of the specified type.
    /// </summary>
    /// <typeparam name="T">The type to check for.</typeparam>
    /// <returns><c>true</c> if the context contains a value of type <typeparamref name="T"/>; otherwise, <c>false</c>.</returns>
    bool Contains<T>();

    /// <summary>
    /// Removes a value of the specified type from the context.
    /// </summary>
    /// <typeparam name="T">The type of the value to remove.</typeparam>
    /// <remarks>
    /// If no value of the specified type exists in the context, this method has no effect.
    /// </remarks>
    void Remove<T>();
}

/// <summary>
/// Provides a default implementation of the <see cref="IHandleContextData"/> interface.
/// </summary>
/// <remarks>
/// This class uses a dictionary to store context data, with the type as the key and the value as the value.
/// </remarks>
public class HandleContextData : IHandleContextData
{
    private readonly Dictionary<Type, object> _data = new();

    /// <inheritdoc />
    public T Get<T>()
    {
        return (T) _data[typeof(T)];
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