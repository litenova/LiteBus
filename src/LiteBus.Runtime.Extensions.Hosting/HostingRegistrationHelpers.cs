using System;
using System.Collections.Generic;

namespace LiteBus.Runtime.Extensions.Hosting;

/// <summary>
///     Shared helpers for generic-host registration from module manifests.
/// </summary>
internal static class HostingRegistrationHelpers
{
    /// <summary>
    ///     Returns types in first-seen order while skipping duplicates.
    /// </summary>
    /// <param name="types">The types to deduplicate.</param>
    /// <returns>The deduplicated type list.</returns>
    public static List<Type> DeduplicatePreserveOrder(IReadOnlyList<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var result = new List<Type>(types.Count);
        var registeredTypes = new HashSet<Type>();

        foreach (var implementationType in types)
        {
            if (registeredTypes.Add(implementationType))
            {
                result.Add(implementationType);
            }
        }

        return result;
    }
}
