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
    ///     Stores context values keyed by their CLR type.
    /// </summary>
    private readonly Dictionary<Type, object> _data = [];

    /// <summary>
    ///     Guards <see cref="_data" /> against concurrent access by parallel event handlers.
    /// </summary>
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public T Get<T>()
    {
        lock (_gate)
        {
            return _data.TryGetValue(typeof(T), out var value)
                ? (T) value
                : throw new HandleContextDataNotFoundException(typeof(T));
        }
    }

    /// <inheritdoc />
    public bool TryGet<T>([MaybeNullWhen(false)] out T value)
    {
        lock (_gate)
        {
            if (_data.TryGetValue(typeof(T), out var stored))
            {
                value = (T) stored;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <inheritdoc />
    public void Set<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            _data[typeof(T)] = value;
        }
    }

    /// <inheritdoc />
    public bool Contains<T>()
    {
        lock (_gate)
        {
            return _data.ContainsKey(typeof(T));
        }
    }

    /// <inheritdoc />
    public void Remove<T>()
    {
        lock (_gate)
        {
            _data.Remove(typeof(T));
        }
    }
}
