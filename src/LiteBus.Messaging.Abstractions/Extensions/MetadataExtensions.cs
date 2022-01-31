using System.Collections.Generic;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Abstractions.Extensions;

public static class MetadataExtensions
{
    public static bool IsAsynchronous(this IHandlerDescriptor descriptor)
    {
        return descriptor.MessageResultType.IsAssignableTo(typeof(Task));
    }

    public static bool IsAsynchronousEnumerable(this IHandlerDescriptor descriptor)
    {
        return descriptor.MessageResultType.IsGenericType &&
               descriptor.MessageResultType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>);
    }

    public static bool IsSynchronous(this IHandlerDescriptor descriptor)
    {
        return !descriptor.IsAsynchronous() && !descriptor.IsAsynchronousEnumerable();
    }
}