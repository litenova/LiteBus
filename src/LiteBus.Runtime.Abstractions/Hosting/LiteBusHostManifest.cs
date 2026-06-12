using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    ///     Creates a manifest snapshot from module configuration after all modules have been built.
    /// </summary>
    /// <param name="moduleConfiguration">The module configuration that collected host registrations.</param>
    /// <returns>A manifest describing startup tasks, background services, and diagnostic probes.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleConfiguration" /> is <see langword="null" />.
    /// </exception>
    public static LiteBusHostManifest FromConfiguration(IModuleConfiguration moduleConfiguration)
    {
        ArgumentNullException.ThrowIfNull(moduleConfiguration);

        return new LiteBusHostManifest(
            moduleConfiguration.StartupTasks.ToList(),
            moduleConfiguration.BackgroundServices.ToList(),
            moduleConfiguration.DiagnosticChecks.ToList());
    }
}