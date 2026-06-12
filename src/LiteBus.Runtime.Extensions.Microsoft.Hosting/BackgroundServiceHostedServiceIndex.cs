using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Maps manifest background service implementation types to their generic-host adapters.
/// </summary>
internal sealed class BackgroundServiceHostedServiceIndex
{
    /// <summary>
    ///     Hosted service adapters keyed by background service implementation type.
    /// </summary>
    private readonly Dictionary<Type, IHostedService> _hostedServicesByImplementationType = new();

    /// <summary>
    ///     Records the hosted service adapter for a background service implementation type.
    /// </summary>
    /// <param name="implementationType">The background service implementation type.</param>
    /// <param name="hostedService">The generic-host adapter executing the background service.</param>
    internal void Register(Type implementationType, IHostedService hostedService)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(hostedService);

        _hostedServicesByImplementationType[implementationType] = hostedService;
    }

    /// <summary>
    ///     Resolves the hosted service adapter for the specified background service implementation type.
    /// </summary>
    /// <typeparam name="TBackgroundService">The background service implementation type.</typeparam>
    /// <returns>The generic-host adapter for <typeparamref name="TBackgroundService" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no hosted service adapter was registered for <typeparamref name="TBackgroundService" />.
    /// </exception>
    internal IHostedService GetHostedService<TBackgroundService>()
        where TBackgroundService : class, IBackgroundService
    {
        if (_hostedServicesByImplementationType.TryGetValue(typeof(TBackgroundService), out var hostedService))
        {
            return hostedService;
        }

        throw new InvalidOperationException(
            $"No hosted service adapter is registered for background service '{typeof(TBackgroundService).FullName}'. " +
            "Ensure the module registered the background service through the LiteBus host manifest.");
    }
}
