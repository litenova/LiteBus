using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Resolution;

public static class LazyInstancesExtensions
{
    public static IInstances<TInstance, TDescriptor> ToInstances<TInstance, TDescriptor>(
        this IEnumerable<LazyInstance<TInstance, TDescriptor>> instances)
    {
        return new LazyInstances<TInstance, TDescriptor>(instances);
    }
}