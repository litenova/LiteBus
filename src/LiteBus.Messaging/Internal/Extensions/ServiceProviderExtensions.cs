using System;

namespace LiteBus.Messaging.Internal.Extensions;

public static class ServiceProviderExtensions
{
    /// <summary>
    /// Retrieves the required service of type <typeparamref name="T"/> from the <see cref="IServiceProvider"/>.
    /// Throws an <see cref="InvalidOperationException"/> if the service is not registered.
    /// </summary>
    /// <typeparam name="T">The type of the service to retrieve.</typeparam>
    /// <param name="serviceProvider">The service provider instance.</param>
    /// <returns>The required service of type <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the service is not registered.</exception>
    public static T GetRequiredService<T>(this IServiceProvider serviceProvider)
    {
        return (T) GetRequiredService(serviceProvider, typeof(T));
    }

    /// <summary>
    /// Retrieves the required service of the specified <paramref name="serviceType"/> from the <see cref="IServiceProvider"/>.
    /// Throws an <see cref="InvalidOperationException"/> if the service is not registered.
    /// </summary>
    /// <param name="serviceProvider">The service provider instance.</param>
    /// <param name="serviceType">The type of the service to retrieve.</param>
    /// <returns>The required service of the specified <paramref name="serviceType"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the service is not registered.</exception>
    public static object GetRequiredService(this IServiceProvider serviceProvider, Type serviceType)
    {
        var service = serviceProvider.GetService(serviceType);

        if (service == null)
        {
            throw new InvalidOperationException($"The required service of type '{serviceType.FullName}' is not registered.");
        }

        return service;
    }
}