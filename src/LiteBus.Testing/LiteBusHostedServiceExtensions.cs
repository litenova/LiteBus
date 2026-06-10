using AwesomeAssertions;
using LiteBus.Inbox;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Testing;

/// <summary>
///     Starts and stops LiteBus manifest-hosted services in integration tests.
/// </summary>
public static class LiteBusHostedServiceExtensions
{
    /// <summary>
    ///     Starts every <see cref="IHostedService" /> registered by <c>AddLiteBus</c>.
    /// </summary>
    /// <param name="provider">The service provider built with LiteBus modules.</param>
    /// <param name="cancellationToken">A token that cancels host startup.</param>
    /// <returns>A task that completes after each hosted service has started.</returns>
    public static async Task StartLiteBusHostedServicesAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Stops every <see cref="IHostedService" /> in reverse registration order.
    /// </summary>
    /// <param name="provider">The service provider built with LiteBus modules.</param>
    /// <param name="cancellationToken">A token that cancels host shutdown.</param>
    /// <returns>A task that completes after each hosted service has stopped.</returns>
    public static async Task StopLiteBusHostedServicesAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        for (var index = hostedServices.Count - 1; index >= 0; index--)
        {
            await hostedServices[index].StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Resolves the generic-host adapter for <see cref="InboxProcessorBackgroundService" />.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled inbox processor.</param>
    /// <returns>The <see cref="IHostedService" /> that runs the inbox processor loop.</returns>
    public static IHostedService GetInboxProcessorHostedService(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        var processorIndex = -1;
        for (var index = 0; index < manifest.BackgroundServices.Count; index++)
        {
            if (manifest.BackgroundServices[index] == typeof(InboxProcessorBackgroundService))
            {
                processorIndex = index;
                break;
            }
        }

        if (processorIndex < 0)
        {
            throw new InvalidOperationException(
                "Inbox processor background service is not registered in the LiteBus host manifest.");
        }

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var backgroundServiceOffset = manifest.StartupTasks.Count > 0 ? 1 : 0;

        return hostedServices[backgroundServiceOffset + processorIndex];
    }

    /// <summary>
    ///     Asserts manifest background service registrations match the expected implementation types.
    /// </summary>
    /// <param name="provider">The service provider built with LiteBus modules.</param>
    /// <param name="expectedBackgroundServices">The expected background service implementation types.</param>
    /// <returns>The resolved <see cref="LiteBusHostManifest" /> for further assertions.</returns>
    public static LiteBusHostManifest AssertBackgroundServices(
        IServiceProvider provider,
        params Type[] expectedBackgroundServices)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(expectedBackgroundServices);

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().BeEquivalentTo(expectedBackgroundServices);
        return manifest;
    }
}
