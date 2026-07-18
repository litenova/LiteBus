using System;
using System.Collections.Generic;
using Autofac;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Applies diagnostic probe registrations from module configuration to an Autofac container builder.
/// </summary>
public static class AutofacDiagnosticHostingExtensions
{
    /// <summary>
    ///     Registers diagnostic probe implementation types collected on the module configuration manifest.
    /// </summary>
    /// <param name="builder">The container builder receiving diagnostic probe registrations.</param>
    /// <param name="diagnosticChecks">The diagnostic probe descriptors registered by modules.</param>
    public static void RegisterDiagnosticChecks(
        this ContainerBuilder builder,
        IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(diagnosticChecks);

        var registeredTypes = new HashSet<Type>();

        foreach (var descriptor in diagnosticChecks)
        {
            if (!registeredTypes.Add(descriptor.ImplementationType))
            {
                continue;
            }

            builder.RegisterType(descriptor.ImplementationType)
                .AsSelf()
                .SingleInstance();
        }
    }
}
