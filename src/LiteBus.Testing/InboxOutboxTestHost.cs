using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Testing;

/// <summary>
///     Builds an isolated LiteBus service provider for inbox and outbox integration tests.
/// </summary>
public sealed class InboxOutboxTestHost : IAsyncDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxOutboxTestHost" /> class.
    /// </summary>
    /// <param name="services">The configured service provider.</param>
    private InboxOutboxTestHost(ServiceProvider services)
    {
        Services = services;
    }

    /// <summary>
    ///     Gets the root service provider for the test host.
    /// </summary>
    public ServiceProvider Services { get; }

    /// <summary>
    ///     Creates a host with in-memory inbox and outbox storage and optional module configuration.
    /// </summary>
    /// <param name="configureRegistry">An optional LiteBus module registry configuration callback.</param>
    /// <param name="configureServices">An optional service collection callback.</param>
    /// <param name="timeProvider">An optional clock used by inbox and outbox writers.</param>
    /// <returns>A disposable test host.</returns>
    public static InboxOutboxTestHost Create(
        Action<IModuleRegistry>? configureRegistry = null,
        Action<IServiceCollection>? configureServices = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddInboxModule(inbox => inbox.UseInMemoryStorage());
            registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());
            configureRegistry?.Invoke(registry);
        });

        configureServices?.Invoke(services);

        return new InboxOutboxTestHost(services.BuildServiceProvider());
    }

    /// <summary>
    ///     Gets the inbox writer from the host.
    /// </summary>
    /// <returns>The configured <see cref="IInbox" /> instance.</returns>
    public IInbox GetInbox() => Services.GetRequiredService<IInbox>();

    /// <summary>
    ///     Gets the outbox writer from the host.
    /// </summary>
    /// <returns>The configured <see cref="IOutbox" /> instance.</returns>
    public IOutbox GetOutbox() => Services.GetRequiredService<IOutbox>();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return Services.DisposeAsync();
    }
}
