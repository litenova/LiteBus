using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Extensions;

/// <summary>
///     Extension methods for handler enumeration helpers.
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    ///     Materializes lazy handler entries into a read-only collection for mediation.
    /// </summary>
    /// <typeparam name="THandler">The handler service type.</typeparam>
    /// <typeparam name="TDescriptor">The handler descriptor type.</typeparam>
    /// <param name="source">The lazy handler sequence.</param>
    /// <returns>A read-only lazy handler collection.</returns>
    public static ILazyHandlerCollection<THandler, TDescriptor> ToLazyReadOnlyCollection<THandler, TDescriptor>(this IEnumerable<LazyHandler<THandler, TDescriptor>> source)
        where TDescriptor : IHandlerDescriptor
    {
        return new LazyHandlerCollection<THandler, TDescriptor>(source);
    }
}
