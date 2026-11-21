using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Validates a LiteBus host composition by resolving its registrations from an already built service provider.
/// </summary>
public static class LiteBusHostValidationExtensions
{
    /// <summary>
    ///     Resolves every LiteBus host registration from the host's root provider without starting or running anything.
    /// </summary>
    /// <param name="services">The root service provider built by the host.</param>
    /// <remarks>
    ///     <para>
    ///         Scope validation comes from the host's own <c>ServiceProviderOptions</c>, which is on by default in the
    ///         Development environment. The method does not build or dispose a provider.
    ///     </para>
    ///     <para>
    ///         It makes no database or broker connection unless a service's constructor does.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="LiteBusHostValidationException">
    ///     Thrown when a host registration cannot be resolved. Every failure is collected before this exception is
    ///     thrown.
    /// </exception>
    public static void ValidateLiteBusHost(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        List<string> failures = [];

        var manifestInstance = Resolve(services, failures, "host manifest", typeof(LiteBusHostManifest));
        var manifest = manifestInstance as LiteBusHostManifest;

        if (manifest is not null)
        {
            foreach (var startupTaskType in manifest.StartupTasks)
            {
                Resolve(
                    services,
                    failures,
                    $"startup task '{startupTaskType.FullName ?? startupTaskType.Name}'",
                    startupTaskType);
            }

            foreach (var backgroundServiceType in manifest.BackgroundServices)
            {
                Resolve(
                    services,
                    failures,
                    $"background service '{backgroundServiceType.FullName ?? backgroundServiceType.Name}'",
                    backgroundServiceType);
            }

            foreach (var descriptor in manifest.DiagnosticChecks)
            {
                var implementationType = descriptor.ImplementationType;
                Resolve(
                    services,
                    failures,
                    $"diagnostic check '{implementationType.FullName ?? implementationType.Name}'",
                    implementationType);
            }
        }

        if (Resolve(
                services,
                failures,
                "hosted services",
                typeof(IEnumerable<IHostedService>)) is IEnumerable<IHostedService> hostedServices)
        {
            foreach (var hostedService in hostedServices)
            {
                _ = hostedService;
            }
        }

        if (failures.Count > 0)
        {
            throw new LiteBusHostValidationException(failures);
        }
    }

#pragma warning disable CA1031 // A validation helper records failures of every type so one call reports all problems.
    /// <summary>
    ///     Resolves one service type and records a failure when it is missing or cannot be constructed.
    /// </summary>
    /// <param name="services">The root service provider to resolve from.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="description">The description of the target being resolved.</param>
    /// <param name="serviceType">The service type to resolve.</param>
    /// <returns>The resolved instance, or <see langword="null" /> when resolution failed.</returns>
    private static object? Resolve(
        IServiceProvider services,
        List<string> failures,
        string description,
        Type serviceType)
    {
        try
        {
            var instance = services.GetService(serviceType);
            if (instance is null)
            {
                failures.Add($"{description}: no service of type '{serviceType.FullName ?? serviceType.Name}' is registered.");
            }

            return instance;
        }
        catch (Exception exception)
        {
            failures.Add(FormatFailure(description, exception));
            return null;
        }
    }
#pragma warning restore CA1031

    /// <summary>
    ///     Formats one failure with the target description, the exception type, and the exception message.
    /// </summary>
    /// <param name="description">The description of the target that failed.</param>
    /// <param name="exception">The exception raised while resolving the target.</param>
    /// <returns>The formatted failure message.</returns>
    private static string FormatFailure(string description, Exception exception)
    {
        return $"{description}: {exception.GetType().FullName}: {exception.Message}";
    }
}
