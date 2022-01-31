using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Resolution;

public class LazyInstances<TInstance, TDescriptor> : IInstances<TInstance, TDescriptor>
{
    private readonly List<LazyInstance<TInstance, TDescriptor>> _instances;

    public LazyInstances(IEnumerable<LazyInstance<TInstance, TDescriptor>> instances)
    {
        _instances = new List<LazyInstance<TInstance, TDescriptor>>(instances);
    }

    public IEnumerator<IInstance<TInstance, TDescriptor>> GetEnumerator() => _instances.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => _instances.Count;
}