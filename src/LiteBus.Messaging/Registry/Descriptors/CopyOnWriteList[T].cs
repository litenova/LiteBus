using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     A list that publishes a new array on every write, so a reader always enumerates one consistent snapshot.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
///     <para>
///         A message descriptor is read by every mediation of its message without a lock, and the registry can still link
///         handlers to it after composition, when a message type is first registered at dispatch. A write therefore copies
///         the current array, changes the copy, and publishes it in one reference assignment. A reader that took the
///         previous snapshot keeps enumerating it unchanged.
///     </para>
///     <para>
///         Writers are not synchronized with each other. The registry serializes every write under its own lock, which is
///         the only caller that changes a descriptor.
///     </para>
/// </remarks>
internal sealed class CopyOnWriteList<T> : IReadOnlyCollection<T>
{
    /// <summary>
    ///     Holds the current snapshot. It is replaced on every write and never changed in place.
    /// </summary>
    private T[] _items = [];

    /// <summary>
    ///     Gets the current snapshot, which later writes never change.
    /// </summary>
    public IReadOnlyCollection<T> Snapshot => Volatile.Read(ref _items);

    /// <inheritdoc />
    public int Count => Volatile.Read(ref _items).Length;

    /// <summary>
    ///     Appends an item by publishing a new snapshot that ends with it.
    /// </summary>
    /// <param name="item">The item to append.</param>
    public void Add(T item)
    {
        var current = _items;
        var next = new T[current.Length + 1];
        current.CopyTo(next, 0);
        next[^1] = item;
        Volatile.Write(ref _items, next);
    }

    /// <summary>
    ///     Removes every item that matches a predicate by publishing a new snapshot without them.
    /// </summary>
    /// <param name="match">The predicate that selects the items to remove.</param>
    public void RemoveAll(Predicate<T> match)
    {
        var current = _items;
        var next = Array.FindAll(current, item => !match(item));

        if (next.Length != current.Length)
        {
            Volatile.Write(ref _items, next);
        }
    }

    /// <summary>
    ///     Determines whether any item in the current snapshot matches a predicate.
    /// </summary>
    /// <param name="match">The predicate to test.</param>
    /// <returns><see langword="true" /> when at least one item matches.</returns>
    public bool Exists(Predicate<T> match)
    {
        return Array.Exists(Volatile.Read(ref _items), match);
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>) Volatile.Read(ref _items)).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
