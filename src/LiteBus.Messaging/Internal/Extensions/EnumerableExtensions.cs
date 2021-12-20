using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Extensions;

public static class EnumerableExtensions
{
    public static ILazyReadOnlyCollection<T> ToLazyReadOnlyCollection<T>(this IEnumerable<Lazy<T>> source)
    {
        return new LazyReadOnlyCollection<T>(source);
    }
}