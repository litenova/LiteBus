using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Splits module background service registrations into startup-phase and continuous host loops.
/// </summary>
internal static class BackgroundServiceHostingRegistrationSplitter
{
    /// <summary>
    ///     Splits registered background service types while preserving first-registration order and deduplicating types.
    /// </summary>
    /// <param name="backgroundServices">The background service types registered by modules.</param>
    /// <returns>The split registration groups.</returns>
    public static BackgroundServiceHostingRegistration Split(IReadOnlyList<Type> backgroundServices)
    {
        ArgumentNullException.ThrowIfNull(backgroundServices);

        var startupInitializerTypes = new List<Type>();
        var continuousServiceTypes = new List<Type>();
        var registeredTypes = new HashSet<Type>();

        foreach (var implementationType in backgroundServices)
        {
            if (!registeredTypes.Add(implementationType))
            {
                continue;
            }

            if (typeof(IBackgroundServiceStartupInitializer).IsAssignableFrom(implementationType))
            {
                startupInitializerTypes.Add(implementationType);
            }
            else
            {
                continuousServiceTypes.Add(implementationType);
            }
        }

        return new BackgroundServiceHostingRegistration(startupInitializerTypes, continuousServiceTypes);
    }
}
