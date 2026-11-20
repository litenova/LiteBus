using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Testing;

/// <summary>
///     Validates a LiteBus composition by building its service provider and resolving its host registrations without
///     starting a host.
/// </summary>
/// <remarks>
///     <para>
///         This helper catches scope violations, missing registrations, and services that cannot be constructed. A
///         singleton background service that captures a scoped service fails under scope validation, and a service whose
///         constructor needs an unregistered type fails while the provider is built or while the service is resolved.
///     </para>
///     <para>
///         It does not run work that needs a running host. Startup tasks, background services, diagnostic checks, and
///         hosted services are resolved but never started. It makes no database or broker connection.
///     </para>
/// </remarks>
public static class LiteBusHostValidation
{
    /// <summary>
    ///     Builds the service provider with scope and build validation enabled, then resolves every LiteBus host
    ///     registration from the root provider.
    /// </summary>
    /// <param name="services">The service collection holding the LiteBus composition to validate.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="LiteBusHostValidationException">
    ///     Thrown when the provider cannot be built or a host registration cannot be resolved. Every failure is collected
    ///     before this exception is thrown.
    /// </exception>
    public static void Validate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        List<string> failures = [];
        ServiceProvider provider;

        try
        {
            provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }
        catch (AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
            {
                failures.Add(FormatFailure("service provider build", innerException));
            }

            throw new LiteBusHostValidationException(failures);
        }
        catch (InvalidOperationException exception)
        {
            failures.Add(FormatFailure("service provider build", exception));
            throw new LiteBusHostValidationException(failures);
        }

        using (provider)
        {
            LiteBusHostManifest? manifest = null;

            Resolve(failures, "host manifest", () => manifest = provider.GetRequiredService<LiteBusHostManifest>());

            if (manifest is not null)
            {
                foreach (var startupTaskType in manifest.StartupTasks)
                {
                    Resolve(
                        failures,
                        $"startup task '{startupTaskType.FullName ?? startupTaskType.Name}'",
                        () => provider.GetRequiredService(startupTaskType));
                }

                foreach (var backgroundServiceType in manifest.BackgroundServices)
                {
                    Resolve(
                        failures,
                        $"background service '{backgroundServiceType.FullName ?? backgroundServiceType.Name}'",
                        () => provider.GetRequiredService(backgroundServiceType));
                }

                foreach (var descriptor in manifest.DiagnosticChecks)
                {
                    var implementationType = descriptor.ImplementationType;
                    Resolve(
                        failures,
                        $"diagnostic check '{implementationType.FullName ?? implementationType.Name}'",
                        () => provider.GetRequiredService(implementationType));
                }
            }

            Resolve(failures, "hosted services", () => _ = provider.GetServices<IHostedService>().ToList());
        }

        if (failures.Count > 0)
        {
            throw new LiteBusHostValidationException(failures);
        }
    }

#pragma warning disable CA1031 // A validation helper records failures of every type so one call reports all problems.
    /// <summary>
    ///     Runs one resolution and records its exception without stopping the remaining resolutions.
    /// </summary>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="description">The description of the target being resolved.</param>
    /// <param name="resolve">The resolution to run.</param>
    private static void Resolve(List<string> failures, string description, Action resolve)
    {
        try
        {
            resolve();
        }
        catch (Exception exception)
        {
            failures.Add(FormatFailure(description, exception));
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
