using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Transport.IntegrationTesting;

/// <summary>
///     Builds an in-memory inbox processor host for component-level integration tests.
/// </summary>
public sealed class InboxProcessorComponentHost : IAsyncDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessorComponentHost" /> class.
    /// </summary>
    /// <param name="services">The configured service provider.</param>
    private InboxProcessorComponentHost(ServiceProvider services)
    {
        Services = services;
    }

    /// <summary>
    ///     Gets the root service provider for the component host.
    /// </summary>
    public ServiceProvider Services { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return Services.DisposeAsync();
    }

    /// <summary>
    ///     Creates a host with in-memory storage, in-process dispatch, and an enabled inbox processor loop.
    /// </summary>
    /// <param name="configureInbox">An optional inbox module builder callback.</param>
    /// <param name="configureRegistry">An optional LiteBus module registry callback invoked after the default inbox wiring.</param>
    /// <param name="configureServices">An optional service collection callback.</param>
    /// <param name="configureHost">An optional inbox processor host options callback.</param>
    /// <returns>A disposable component host.</returns>
    public static InboxProcessorComponentHost Create(
        Action<InboxModuleBuilder>? configureInbox = null,
        Action<IModuleRegistry>? configureRegistry = null,
        Action<IServiceCollection>? configureServices = null,
        Action<InboxProcessorHostOptions>? configureHost = null)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.UseInProcessDispatch();
                inbox.EnableInboxProcessor(configureHost);
                configureInbox?.Invoke(inbox);
            });

            configureRegistry?.Invoke(registry);
        });

        configureServices?.Invoke(services);

        return new InboxProcessorComponentHost(services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
    }

    /// <summary>
    ///     Gets the inbox writer from the host.
    /// </summary>
    /// <returns>The configured <see cref="IInbox" /> instance.</returns>
    public IInbox GetInbox()
    {
        return Services.GetRequiredService<IInbox>();
    }

    /// <summary>
    ///     Gets the in-memory inbox store backing the host.
    /// </summary>
    /// <returns>The configured <see cref="InMemoryInboxStore" /> instance.</returns>
    public InMemoryInboxStore GetStore()
    {
        return Services.GetRequiredService<InMemoryInboxStore>();
    }

    /// <summary>
    ///     Gets the inbox processor control surface used for drain and pause operations.
    /// </summary>
    /// <returns>The configured <see cref="IInboxProcessorControl" /> instance.</returns>
    public IInboxProcessorControl GetProcessorControl()
    {
        return Services.GetRequiredService<IInboxProcessorControl>();
    }

    /// <summary>
    ///     Starts every LiteBus hosted service registered by <c>AddLiteBus</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels host startup.</param>
    /// <returns>A task that completes after hosted services have started.</returns>
    public Task StartHostedServicesAsync(CancellationToken cancellationToken)
    {
        return LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(Services, cancellationToken);
    }

    /// <summary>
    ///     Stops every LiteBus hosted service in reverse registration order.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels host shutdown.</param>
    /// <returns>A task that completes after hosted services have stopped.</returns>
    public Task StopHostedServicesAsync(CancellationToken cancellationToken)
    {
        return LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(Services, cancellationToken);
    }
}