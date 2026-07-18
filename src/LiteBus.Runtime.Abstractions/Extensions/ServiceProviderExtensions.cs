using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.Abstractions.Extensions;

/// <summary>
///     Resolves required services without depending on a specific dependency injection container.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    ///     Resolves a required service from an <see cref="IServiceProvider" />.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="serviceProvider">The service provider used for resolution.</param>
    /// <returns>The resolved service.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">Thrown when the service is not registered.</exception>
    public static T GetRequiredService<T>(this IServiceProvider serviceProvider)
    {
        return (T) serviceProvider.GetRequiredService(typeof(T));
    }

    /// <summary>
    ///     Resolves a required service from an <see cref="IServiceProvider" />.
    /// </summary>
    /// <param name="serviceProvider">The service provider used for resolution.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The resolved service.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">Thrown when the service is not registered.</exception>
    public static object GetRequiredService(this IServiceProvider serviceProvider, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceProvider.GetService(serviceType) ?? throw new LiteBusDependencyResolutionException(serviceType);
    }
}
