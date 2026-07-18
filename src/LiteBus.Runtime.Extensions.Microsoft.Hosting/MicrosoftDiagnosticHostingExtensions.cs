using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Applies diagnostic probe registrations from module configuration to a Microsoft dependency injection service
///     collection.
/// </summary>
public static class MicrosoftDiagnosticHostingExtensions
{
    /// <summary>
    ///     Registers diagnostic probe implementation types collected on the module configuration manifest.
    /// </summary>
    /// <param name="services">The service collection receiving diagnostic probe registrations.</param>
    /// <param name="diagnosticChecks">The diagnostic probe descriptors registered by modules.</param>
    public static void RegisterDiagnosticChecks(
        this IServiceCollection services,
        IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(diagnosticChecks);

        var registeredTypes = new HashSet<Type>();

        foreach (var descriptor in diagnosticChecks)
        {
            if (!registeredTypes.Add(descriptor.ImplementationType))
            {
                continue;
            }

            services.Add(ServiceDescriptor.Singleton(descriptor.ImplementationType, descriptor.ImplementationType));
        }
    }
}
