using System;
using System.Collections.Generic;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
    /// <remarks>
    ///     <para>
    ///         This method builds and disposes its own provider, so call it on a composition built only for the test,
    ///         never on a collection the host builds afterwards. A provider that holds state from the composition breaks
    ///         the host's own build when it is disposed first.
    ///     </para>
    ///     <para>
    ///         Inside a host, call <see cref="LiteBusHostValidationExtensions.ValidateLiteBusHost(IServiceProvider)" /> on the built host's
    ///         <c>Services</c> instead.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="LiteBusHostValidationException">
    ///     Thrown when the provider cannot be built or a host registration cannot be resolved. Every failure is collected
    ///     before this exception is thrown.
    /// </exception>
    public static void Validate(IServiceCollection services)
    {
        Validate(services, options: null);
    }

    /// <summary>
    ///     Builds the service provider with scope and build validation enabled, then validates the LiteBus host
    ///     composition from the root provider.
    /// </summary>
    /// <param name="services">The service collection holding the LiteBus composition to validate.</param>
    /// <param name="options">
    ///     The checks to run, or <see langword="null" /> to run every check. Each check is individually switchable.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         This method builds and disposes its own provider, so call it on a composition built only for the test,
    ///         never on a collection the host builds afterwards. A provider that holds state from the composition breaks
    ///         the host's own build when it is disposed first.
    ///     </para>
    ///     <para>
    ///         Inside a host, call <see cref="LiteBusHostValidationExtensions.ValidateLiteBusHost(IServiceProvider, LiteBusHostValidationOptions)" /> on the built host's
    ///         <c>Services</c> instead.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="LiteBusHostValidationException">
    ///     Thrown when the provider cannot be built or a check fails. Every failure is collected before this exception
    ///     is thrown.
    /// </exception>
    public static void Validate(IServiceCollection services, LiteBusHostValidationOptions? options)
    {
        var result = ValidateWithResult(services, options);

        if (result.Failures.Count > 0)
        {
            throw new LiteBusHostValidationException(result.Failures);
        }
    }

    /// <summary>
    ///     Builds the service provider with scope and build validation enabled, then validates the LiteBus host
    ///     composition and answers the outcome without throwing.
    /// </summary>
    /// <param name="services">The service collection holding the LiteBus composition to validate.</param>
    /// <returns>The validation outcome, with one entry per failure and per skipped round trip.</returns>
    /// <remarks>
    ///     <para>
    ///         This method builds and disposes its own provider, so call it on a composition built only for the test,
    ///         never on a collection the host builds afterwards. A provider that holds state from the composition breaks
    ///         the host's own build when it is disposed first.
    ///     </para>
    ///     <para>
    ///         Runs every check. The skipped round trips are only visible on the answered result.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    public static LiteBusHostValidationResult ValidateWithResult(IServiceCollection services)
    {
        return ValidateWithResult(services, options: null);
    }

    /// <summary>
    ///     Builds the service provider with scope and build validation enabled, then validates the LiteBus host
    ///     composition and answers the outcome without throwing.
    /// </summary>
    /// <param name="services">The service collection holding the LiteBus composition to validate.</param>
    /// <param name="options">
    ///     The checks to run, or <see langword="null" /> to run every check. Each check is individually switchable.
    /// </param>
    /// <returns>The validation outcome, with one entry per failure and per skipped round trip.</returns>
    /// <remarks>
    ///     <para>
    ///         This method builds and disposes its own provider, so call it on a composition built only for the test,
    ///         never on a collection the host builds afterwards. A provider that holds state from the composition breaks
    ///         the host's own build when it is disposed first.
    ///     </para>
    ///     <para>
    ///         Inside a host, call <see cref="LiteBusHostValidationExtensions.ValidateLiteBusHostWithResult(IServiceProvider, LiteBusHostValidationOptions)" /> on the built host's
    ///         <c>Services</c> instead.
    ///     </para>
    ///     <para>
    ///         The overload to reach for when missing samples do not fail validation: the skipped round trips are
    ///         only visible on the answered result.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    public static LiteBusHostValidationResult ValidateWithResult(
        IServiceCollection services,
        LiteBusHostValidationOptions? options)
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

            return new LiteBusHostValidationResult(failures, []);
        }
        catch (InvalidOperationException exception)
        {
            failures.Add(FormatFailure("service provider build", exception));

            return new LiteBusHostValidationResult(failures, []);
        }

        using (provider)
        {
            return provider.ValidateLiteBusHostWithResult(options);
        }
    }

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
