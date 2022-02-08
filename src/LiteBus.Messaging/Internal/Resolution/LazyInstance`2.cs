using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Resolution;

public class LazyInstance<TInstance, TDescriptor> : IInstance<TInstance, TDescriptor>
{
    private readonly Lazy<TInstance> _lazyInstance;

    public LazyInstance(Lazy<TInstance> lazyInstance, TDescriptor descriptor)
    {
        _lazyInstance = lazyInstance;
        Descriptor = descriptor;
    }

    public TInstance Instance => _lazyInstance.Value;

    public TDescriptor Descriptor { get; }
}
