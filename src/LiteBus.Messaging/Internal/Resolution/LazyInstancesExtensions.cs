using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Resolution;

public static class LazyInstancesExtensions
{
    public static IInstances<TDescriptor> ToInstances<TDescriptor>(
        this IEnumerable<LazyInstance<TDescriptor>> instances)
    {
        return new LazyInstances<TDescriptor>(instances);
    }
}