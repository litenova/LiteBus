using System;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Resolves generic-host adapters for manifest-registered background services.
/// </summary>
public static class BackgroundServiceHostedServiceExtensions
{
    /// <summary>
    ///     Resolves the <see cref="IHostedService" /> adapter that executes
    ///     <typeparamref name="TBackgroundService" />.
    /// </summary>
    /// <typeparam name="TBackgroundService">The manifest background service implementation type.</typeparam>
    /// <param name="serviceProvider">The built service provider.</param>
    /// <returns>The hosted service adapter registered for <typeparamref name="TBackgroundService" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no adapter was registered for <typeparamref name="TBackgroundService" />.
    /// </exception>
    public static IHostedService GetHostedServiceForBackgroundService<TBackgroundService>(this IServiceProvider serviceProvider)
        where TBackgroundService : class, IBackgroundService
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return serviceProvider.GetRequiredService<BackgroundServiceHostedServiceIndex>()
            .GetHostedService<TBackgroundService>();
    }
}
