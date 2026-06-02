using System;
using System.Collections.Generic;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Holds background service types split into startup-phase and continuous host loops.
/// </summary>
internal sealed class BackgroundServiceHostingRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="BackgroundServiceHostingRegistration" /> class.
    /// </summary>
    /// <param name="startupInitializerTypes">The startup-phase background service types in registration order.</param>
    /// <param name="continuousServiceTypes">The continuous background service types in registration order.</param>
    public BackgroundServiceHostingRegistration(
        IReadOnlyList<Type> startupInitializerTypes,
        IReadOnlyList<Type> continuousServiceTypes)
    {
        StartupInitializerTypes = startupInitializerTypes ?? throw new ArgumentNullException(nameof(startupInitializerTypes));
        ContinuousServiceTypes = continuousServiceTypes ?? throw new ArgumentNullException(nameof(continuousServiceTypes));
    }

    /// <summary>
    ///     Gets the startup-phase background service types in registration order.
    /// </summary>
    public IReadOnlyList<Type> StartupInitializerTypes { get; }

    /// <summary>
    ///     Gets the continuous background service types in registration order.
    /// </summary>
    public IReadOnlyList<Type> ContinuousServiceTypes { get; }
}
