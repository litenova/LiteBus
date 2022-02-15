using System.Collections;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Resolution;

public class LazyInstances<TDescriptor> : IInstances<TDescriptor>
{
    private readonly List<LazyInstance<TDescriptor>> _instances;

    public LazyInstances(IEnumerable<LazyInstance<TDescriptor>> instances)
    {
        _instances = new List<LazyInstance<TDescriptor>>(instances);
    }

    public IEnumerator<IInstance<TDescriptor>> GetEnumerator()
    {
        return _instances.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _instances.Count;
}