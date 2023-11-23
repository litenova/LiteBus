using System;

namespace LiteBus.Messaging.Abstractions;

public struct LazyHandler<THandler, TDescriptor>
{
    public required Lazy<THandler> Handler { get; init; }

    public required TDescriptor Descriptor { get; init; }
}