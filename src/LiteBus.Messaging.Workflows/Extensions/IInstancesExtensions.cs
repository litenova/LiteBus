using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Workflows.Extensions;

public static class InstancesExtensions
{
    public static IEnumerable<IHandler> GetAsyncHandlers<TDescriptor>(this IInstances<TDescriptor> instances)
        where TDescriptor : IHandlerDescriptor
    {
        return instances.Where(i => i.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                        .Select(i => i.Instance);
    }

    public static IEnumerable<IHandler> GetSyncHandlers<TDescriptor>(this IInstances<TDescriptor> instances)
        where TDescriptor : IHandlerDescriptor
    {
        return instances.Where(i => i.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                        .Select(i => i.Instance);
    }
}