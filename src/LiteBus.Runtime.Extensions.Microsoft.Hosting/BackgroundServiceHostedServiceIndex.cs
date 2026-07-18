using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Hosting;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Maps manifest background service implementation types to their resolved instances for manual host testing.
/// </summary>
internal sealed class BackgroundServiceHostedServiceIndex
{
    /// <summary>
    ///     Background service instances keyed by implementation type.
    /// </summary>
    private readonly Dictionary<Type, IBackgroundService> _backgroundServicesByImplementationType = [];

    /// <summary>
    ///     Records the background service instance for an implementation type.
    /// </summary>
    /// <param name="implementationType">The background service implementation type.</param>
    /// <param name="backgroundService">The resolved background service instance.</param>
    internal void Register(Type implementationType, IBackgroundService backgroundService)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(backgroundService);

        _backgroundServicesByImplementationType[implementationType] = backgroundService;
    }

    /// <summary>
    ///     Resolves a hosted service wrapper for the specified background service implementation type.
    /// </summary>
    /// <typeparam name="TBackgroundService">The background service implementation type.</typeparam>
    /// <returns>A generic-host wrapper that executes <typeparamref name="TBackgroundService" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no background service was registered for <typeparamref name="TBackgroundService" />.
    /// </exception>
    internal IHostedService GetHostedService<TBackgroundService>()
        where TBackgroundService : class, IBackgroundService
    {
        if (_backgroundServicesByImplementationType.TryGetValue(typeof(TBackgroundService), out var backgroundService))
        {
            return new BackgroundServiceHostWrapper(backgroundService);
        }

        throw new InvalidOperationException(
            $"No background service is registered for '{typeof(TBackgroundService).FullName}'. " +
            "Ensure the module registered the background service through the LiteBus host manifest.");
    }
}
