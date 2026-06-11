using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Runtime.Abstractions.Hosting;

/// <summary>
///     Describes startup tasks, background services, and diagnostic probes registered during module composition.
/// </summary>
public sealed class LiteBusHostManifest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostManifest" /> class.
    /// </summary>
    /// <param name="startupTasks">The startup task implementation types registered by modules.</param>
    /// <param name="backgroundServices">The background service implementation types registered by modules.</param>
    /// <param name="diagnosticChecks">The diagnostic probe descriptors registered by modules.</param>
    public LiteBusHostManifest(
        IReadOnlyList<Type> startupTasks,
        IReadOnlyList<Type> backgroundServices,
        IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)
    {
        StartupTasks = startupTasks ?? throw new ArgumentNullException(nameof(startupTasks));
        BackgroundServices = backgroundServices ?? throw new ArgumentNullException(nameof(backgroundServices));
        DiagnosticChecks = diagnosticChecks ?? throw new ArgumentNullException(nameof(diagnosticChecks));
    }

    /// <summary>
    ///     Gets the startup task implementation types registered by modules.
    /// </summary>
    /// <value>The startup task types executed before background services start.</value>
    public IReadOnlyList<Type> StartupTasks { get; }

    /// <summary>
    ///     Gets the background service implementation types registered by modules.
    /// </summary>
    /// <value>The background service types executed for the lifetime of the host.</value>
    public IReadOnlyList<Type> BackgroundServices { get; }

    /// <summary>
    ///     Gets the diagnostic probe descriptors registered by modules.
    /// </summary>
    /// <value>The probe descriptors applications can resolve from dependency injection.</value>
    public IReadOnlyList<DiagnosticCheckDescriptor> DiagnosticChecks { get; }
}