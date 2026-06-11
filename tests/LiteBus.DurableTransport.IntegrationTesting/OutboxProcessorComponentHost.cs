using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Builds an in-memory outbox processor host for component-level integration tests.
/// </summary>
public sealed class OutboxProcessorComponentHost : IAsyncDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorComponentHost" /> class.
    /// </summary>
    /// <param name="services">The configured service provider.</param>
    private OutboxProcessorComponentHost(ServiceProvider services)
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
    ///     Creates a host with in-memory storage, in-process dispatch, and an enabled outbox processor loop.
    /// </summary>
    /// <param name="configureOutbox">An optional outbox module builder callback.</param>
    /// <param name="configureRegistry">An optional LiteBus module registry callback invoked after the default outbox wiring.</param>
    /// <param name="configureServices">An optional service collection callback.</param>
    /// <param name="configureHost">An optional outbox processor host options callback.</param>
    /// <returns>A disposable component host.</returns>
    public static OutboxProcessorComponentHost Create(
        Action<OutboxModuleBuilder>? configureOutbox = null,
        Action<IModuleRegistry>? configureRegistry = null,
        Action<IServiceCollection>? configureServices = null,
        Action<OutboxProcessorHostOptions>? configureHost = null)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddOutboxModule(outbox =>
            {
                outbox.UseInMemoryStorage();
                outbox.UseEventOutboxDispatcher();
                outbox.EnableOutboxProcessor(configureHost);
                configureOutbox?.Invoke(outbox);
            });

            configureRegistry?.Invoke(registry);
        });

        configureServices?.Invoke(services);

        return new OutboxProcessorComponentHost(services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
    }

    /// <summary>
    ///     Gets the outbox writer from the host.
    /// </summary>
    /// <returns>The configured <see cref="IOutbox" /> instance.</returns>
    public IOutbox GetOutbox()
    {
        return Services.GetRequiredService<IOutbox>();
    }

    /// <summary>
    ///     Gets the in-memory outbox store backing the host.
    /// </summary>
    /// <returns>The configured <see cref="InMemoryOutboxStore" /> instance.</returns>
    public InMemoryOutboxStore GetStore()
    {
        return Services.GetRequiredService<InMemoryOutboxStore>();
    }

    /// <summary>
    ///     Gets the outbox processor control surface used for drain and pause operations.
    /// </summary>
    /// <returns>The configured <see cref="IOutboxProcessorControl" /> instance.</returns>
    public IOutboxProcessorControl GetProcessorControl()
    {
        return Services.GetRequiredService<IOutboxProcessorControl>();
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