using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <inheritdoc cref="IHandleContextData" />
/// <remarks>
/// <para>
///     Access is guarded by a lock rather than left unsynchronised the way <see cref="IExecutionContext.Items" /> is.
///     Event handlers can run in parallel over one execution context, and a torn dictionary there would surface as a
///     corrupted read a long way from its cause. The lock is uncontended for every sequential mediation, which is all
///     of them on the command and query axes.
/// </para>
/// <para>
///     It ships beside the contract so that anything implementing <see cref="IExecutionContext" />, a test double
///     included, has a working store to hand back from <see cref="IExecutionContext.Data" /> without writing one.
/// </para>
/// </remarks>
public sealed class HandleContextData : IHandleContextData
{
    /// <summary>
    ///     Stores context values keyed by their CLR type and, where the caller supplied one, a key.
    /// </summary>
    private readonly Dictionary<DataKey, object> _data = [];

    /// <summary>
    ///     Guards <see cref="_data" /> against concurrent access by parallel event handlers.
    /// </summary>
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public T Get<T>()
    {
        return Read<T>(DataKey.Unkeyed<T>());
    }

    /// <inheritdoc />
    public bool TryGet<T>([MaybeNullWhen(false)] out T value)
    {
        return TryRead(DataKey.Unkeyed<T>(), out value);
    }

    /// <inheritdoc />
    public void Set<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        Write(DataKey.Unkeyed<T>(), value);
    }

    /// <inheritdoc />
    public bool Contains<T>()
    {
        return Has(DataKey.Unkeyed<T>());
    }

    /// <inheritdoc />
    public void Remove<T>()
    {
        Delete(DataKey.Unkeyed<T>());
    }

    /// <inheritdoc />
    public T Get<T>(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Read<T>(DataKey.Keyed<T>(key));
    }

    /// <inheritdoc />
    public bool TryGet<T>(object key, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryRead(DataKey.Keyed<T>(key), out value);
    }

    /// <inheritdoc />
    public void Set<T>(object key, T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        Write(DataKey.Keyed<T>(key), value);
    }

    /// <inheritdoc />
    public bool Contains<T>(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Has(DataKey.Keyed<T>(key));
    }

    /// <inheritdoc />
    public void Remove<T>(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Delete(DataKey.Keyed<T>(key));
    }

    /// <summary>
    ///     Reads the value stored under a slot, or reports that nothing stored one.
    /// </summary>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    /// <param name="slot">The slot to read.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="HandleContextDataNotFoundException">The slot holds no value.</exception>
    private T Read<T>(DataKey slot)
    {
        lock (_gate)
        {
            return _data.TryGetValue(slot, out var value)
                ? (T) value
                : throw new HandleContextDataNotFoundException(slot.Type, slot.Key);
        }
    }

    /// <summary>
    ///     Attempts to read the value stored under a slot.
    /// </summary>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    /// <param name="slot">The slot to read.</param>
    /// <param name="value">When this method returns <see langword="true" />, the stored value.</param>
    /// <returns><see langword="true" /> when the slot holds a value.</returns>
    private bool TryRead<T>(DataKey slot, [MaybeNullWhen(false)] out T value)
    {
        lock (_gate)
        {
            if (_data.TryGetValue(slot, out var stored))
            {
                value = (T) stored;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    ///     Stores a value in a slot, replacing whatever the slot held.
    /// </summary>
    /// <param name="slot">The slot to write.</param>
    /// <param name="value">The value to store.</param>
    private void Write(DataKey slot, object value)
    {
        lock (_gate)
        {
            _data[slot] = value;
        }
    }

    /// <summary>
    ///     Determines whether a slot holds a value.
    /// </summary>
    /// <param name="slot">The slot to check.</param>
    /// <returns><see langword="true" /> when the slot holds a value.</returns>
    private bool Has(DataKey slot)
    {
        lock (_gate)
        {
            return _data.ContainsKey(slot);
        }
    }

    /// <summary>
    ///     Removes whatever a slot holds.
    /// </summary>
    /// <param name="slot">The slot to clear.</param>
    private void Delete(DataKey slot)
    {
        lock (_gate)
        {
            _data.Remove(slot);
        }
    }

    /// <summary>
    ///     Addresses one slot in the store: a CLR type, and a caller-supplied key where there is one.
    /// </summary>
    /// <param name="Type">The type the value is stored under.</param>
    /// <param name="Key">The caller-supplied key, or <see langword="null" /> for the unkeyed slot.</param>
    /// <remarks>
    ///     The unkeyed slot is a distinct slot rather than a reserved key value, so a keyed entry can never collide
    ///     with the unkeyed one however the caller's key compares. Keys are compared by their own
    ///     <see cref="object.Equals(object)" />, which is what makes an identifier value object usable directly.
    /// </remarks>
    private readonly record struct DataKey(Type Type, object? Key)
    {
        /// <summary>
        ///     Addresses the unkeyed slot for a type.
        /// </summary>
        /// <typeparam name="T">The type the value is stored under.</typeparam>
        /// <returns>The slot.</returns>
        public static DataKey Unkeyed<T>()
        {
            return new DataKey(typeof(T), Key: null);
        }

        /// <summary>
        ///     Addresses a keyed slot for a type.
        /// </summary>
        /// <typeparam name="T">The type the value is stored under.</typeparam>
        /// <param name="key">The caller-supplied key.</param>
        /// <returns>The slot.</returns>
        public static DataKey Keyed<T>(object key)
        {
            return new DataKey(typeof(T), key);
        }
    }
}
