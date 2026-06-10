using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Transport.AzureServiceBus;
using Testcontainers.ServiceBus;

namespace LiteBus.DurableTransport.IntegrationTests.Azure;

/// <summary>
///     Shared Azure Service Bus emulator fixture for durable transport integration tests.
/// </summary>
public sealed class ServiceBusEmulatorFixture : IAsyncLifetime
{
    /// <summary>
    ///     Gets the ingress queue name declared in the emulator configuration.
    /// </summary>
    public const string IngressQueueName = "litebus-ingress";

    /// <summary>
    ///     Gets the dispatch queue name declared in the emulator configuration.
    /// </summary>
    public const string DispatchQueueName = "litebus-dispatch";

    /// <summary>
    ///     Gets the outbox queue name declared in the emulator configuration.
    /// </summary>
    public const string OutboxQueueName = "litebus-outbox";

    /// <summary>
    ///     Gets the ingress failure queue name declared in the emulator configuration.
    /// </summary>
    public const string FailureQueueName = "litebus-fail";

    /// <summary>
    ///     Gets the transport options for the started Service Bus emulator.
    /// </summary>
    public AzureServiceBusTransportOptions TransportOptions { get; private set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the emulator container started successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    ///     The running Service Bus emulator container.
    /// </summary>
    private ServiceBusContainer? _container;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            await DockerTestGate.RunAsync(async () =>
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "Azure", "servicebus-emulator-config.json");
                _container = new ServiceBusBuilder()
                    .WithAcceptLicenseAgreement(true)
                    .WithConfig(configPath)
                    .Build();

                await _container.StartAsync().ConfigureAwait(false);

                TransportOptions = new AzureServiceBusTransportOptions
                {
                    ConnectionString = _container.GetConnectionString()
                };
                IsAvailable = true;
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            IsAvailable = false;
        }
    }

    /// <summary>
    ///     Resolves a pre-declared emulator queue for the supplied scenario prefix.
    /// </summary>
    /// <param name="prefix">The prefix identifying the scenario under test.</param>
    /// <returns>The queue name declared in the emulator configuration.</returns>
    public string ResolveQueue(string prefix) =>
        prefix switch
        {
            "ingress" => IngressQueueName,
            "ingress-fail" or "ingress-store-full" => FailureQueueName,
            "dispatch" or "inbox-dispatch" => DispatchQueueName,
            "outbox-dispatch" or "outbox-route" => OutboxQueueName,
            _ => FailureQueueName
        };

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
