using System.Collections;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal;

public sealed class LazyHandlerCollection<THandler, TDescriptor> : ILazyHandlerCollection<THandler, TDescriptor> where TDescriptor : IHandlerDescriptor
{
    private readonly List<LazyHandler<THandler, TDescriptor>> _list;

    public LazyHandlerCollection(IEnumerable<LazyHandler<THandler, TDescriptor>> source)
    {
        _list = new List<LazyHandler<THandler, TDescriptor>>(source);
    }

    public IEnumerator<LazyHandler<THandler, TDescriptor>> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _list.Count;
}