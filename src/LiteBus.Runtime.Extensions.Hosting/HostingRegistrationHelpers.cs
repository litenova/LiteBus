using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;

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
        HashSet<Type> registeredTypes = [];

        foreach (var implementationType in types)
        {
            if (registeredTypes.Add(implementationType))
            {
                result.Add(implementationType);
            }
        }

        return result;
    }

    /// <summary>
    ///     Determines whether a module already registered a startup task or background service type with a factory or an
    ///     instance, so the host bridge resolves that registration instead of replacing it with a type registration.
    /// </summary>
    /// <param name="registry">The modules' dependency registry, or <see langword="null" /> when it is not available.</param>
    /// <param name="implementationType">The startup task or background service type.</param>
    /// <returns>
    ///     <see langword="true" /> when a module registered the type with a factory or an instance; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     A type registered by type is still replaced with a singleton registration, which keeps one instance of each
    ///     background service across the host and anything that resolves the type directly.
    /// </remarks>
    public static bool IsRegisteredWithFactoryOrInstance(IDependencyRegistry? registry, Type implementationType)
    {
        if (registry is null)
        {
            return false;
        }

        foreach (var descriptor in registry)
        {
            if (descriptor.DependencyType == implementationType && (descriptor.Factory is not null || descriptor.Instance is not null))
            {
                return true;
            }
        }

        return false;
    }
}
