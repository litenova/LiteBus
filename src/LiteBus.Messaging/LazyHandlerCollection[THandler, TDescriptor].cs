using System.Collections;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Read-only collection of lazily resolved handlers for one handler role.
/// </summary>
/// <typeparam name="THandler">The handler service type.</typeparam>
/// <typeparam name="TDescriptor">The handler descriptor type.</typeparam>
public sealed class LazyHandlerCollection<THandler, TDescriptor> : ILazyHandlerCollection<THandler, TDescriptor> where TDescriptor : IHandlerDescriptor
{
    /// <summary>
    ///     Backing list of lazy handler entries in registration order.
    /// </summary>
    private readonly List<LazyHandler<THandler, TDescriptor>> _list;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LazyHandlerCollection{THandler, TDescriptor}" /> class.
    /// </summary>
    /// <param name="source">The lazy handlers to expose.</param>
    public LazyHandlerCollection(IEnumerable<LazyHandler<THandler, TDescriptor>> source)
    {
        _list = new List<LazyHandler<THandler, TDescriptor>>(source);
    }

    /// <inheritdoc />
    public IEnumerator<LazyHandler<THandler, TDescriptor>> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public int Count => _list.Count;
}
