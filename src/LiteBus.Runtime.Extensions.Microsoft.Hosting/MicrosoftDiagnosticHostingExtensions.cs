using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
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
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> or <paramref name="diagnosticChecks" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when one implementation type is listed under two probe names.
    /// </exception>
    /// <remarks>
    ///     The module configuration manifest already refuses one implementation type under two names, so when this
    ///     method is called from <c>AddLiteBus</c> with the manifest descriptors the conflict branch below is
    ///     unreachable. The local tracking guards direct calls with a hand-built descriptor list.
    /// </remarks>
    public static void RegisterDiagnosticChecks(
        this IServiceCollection services,
        IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(diagnosticChecks);

        var registeredNamesByType = new Dictionary<Type, string>();

        foreach (var descriptor in diagnosticChecks)
        {
            if (registeredNamesByType.TryGetValue(descriptor.ImplementationType, out var registeredName))
            {
                if (!string.Equals(registeredName, descriptor.Name, StringComparison.Ordinal))
                {
                    throw new ModuleCompositionException(
                        $"Diagnostic check type '{descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name}' is already registered " +
                        $"as '{registeredName}' and cannot also be registered as '{descriptor.Name}'.");
                }

                continue;
            }

            registeredNamesByType.Add(descriptor.ImplementationType, descriptor.Name);

            services.Add(ServiceDescriptor.Singleton(descriptor.ImplementationType, descriptor.ImplementationType));
        }
    }
}
