using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Resolution;

public class LazyInstance<TDescriptor> : IInstance<TDescriptor>
{
    private readonly Lazy<IHandler> _handlerInstance;

    public LazyInstance(Lazy<IHandler> handlerInstance, TDescriptor descriptor)
    {
        _handlerInstance = handlerInstance;
        Descriptor = descriptor;
    }

    public IHandler Instance => _handlerInstance.Value;

    public TDescriptor Descriptor { get; }
}