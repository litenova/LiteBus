using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Extensions;

public static class EnumerableExtensions
{
    public static ILazyHandlerCollection<THandler, TDescriptor> ToLazyReadOnlyCollection<THandler, TDescriptor>(this IEnumerable<LazyHandler<THandler, TDescriptor>> source)
    {
        return new LazyHandlerCollection<THandler, TDescriptor>(source);
    }
}