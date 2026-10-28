using System;
using System.Collections.Generic;
using Autofac;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="builder" /> or <paramref name="diagnosticChecks" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when one implementation type is listed under two probe names, or when two implementation types share
    ///     one probe name.
    /// </exception>
    /// <remarks>
    ///     The module configuration manifest already refuses both conflict directions, so when this method is called
    ///     from <c>AddLiteBus</c> with the manifest descriptors the conflict branches below are unreachable. The local
    ///     tracking guards direct calls with a hand-built descriptor list.
    /// </remarks>
    public static void RegisterDiagnosticChecks(
        this ContainerBuilder builder,
        IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(diagnosticChecks);

        var registeredNamesByType = new Dictionary<Type, string>();
        var registeredTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);

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

            if (registeredTypesByName.TryGetValue(descriptor.Name, out var registeredType))
            {
                throw new ModuleCompositionException(
                    $"Diagnostic check name '{descriptor.Name}' is already registered by type " +
                    $"'{registeredType.FullName ?? registeredType.Name}' and cannot also be registered by type " +
                    $"'{descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name}'.");
            }

            registeredNamesByType.Add(descriptor.ImplementationType, descriptor.Name);
            registeredTypesByName.Add(descriptor.Name, descriptor.ImplementationType);

            builder.RegisterType(descriptor.ImplementationType)
                .AsSelf()
                .SingleInstance();
        }
    }
}
