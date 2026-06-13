using LiteBus.Transport.Kafka;
using Testcontainers.Kafka;

namespace LiteBus.Transport.IntegrationTesting.Kafka;

/// <summary>
///     Manages a shared Kafka test container for transport integration tests.
/// </summary>
public sealed class KafkaBrokerHost : IAsyncDisposable
{
    private KafkaContainer? _container;

    /// <summary>
    ///     Gets the transport options for the started Kafka container.
    /// </summary>
    public KafkaTransportOptions TransportOptions { get; private set; } = null!;

    /// <summary>
    ///     Starts the Kafka test container when Docker is available.
    /// </summary>
    /// <returns>A task that completes when the broker is ready.</returns>
    /// <remarks>
    ///     Uses the Debian-based Confluent Platform image pinned by <see cref="KafkaBuilder.KafkaImage" /> so
    ///     librdkafka native bindings avoid Alpine musl/glibc mismatches during integration tests.
    /// </remarks>
    public async Task StartAsync()
    {
        await DockerTestGate.RunAsync(async () =>
        {
            _container = new KafkaBuilder()
                .WithImage(KafkaBuilder.KafkaImage)
                .WithVendor(KafkaVendor.Confluent)
                .Build();
            await _container.StartAsync().ConfigureAwait(false);

            TransportOptions = new KafkaTransportOptions
            {
                BootstrapServers = ResolveBootstrapServers(_container),
                ConsumerGroupId = $"litebus-test-{Guid.NewGuid():N}",
                ClientId = "litebus-transport-integration-tests"
            };

            await KafkaTransportTestInfrastructure.WarmupBrokerAsync(TransportOptions.BootstrapServers)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Resolves a librdkafka-compatible bootstrap server list from the running container.
    /// </summary>
    /// <param name="container">The started Kafka container.</param>
    /// <returns>The bootstrap servers in <c>host:port</c> form.</returns>
    /// <remarks>
    ///     <see cref="KafkaContainer.GetBootstrapAddress" /> returns a <c>plaintext://</c> URI that Confluent clients
    ///     mishandle, causing metadata redirects to the container's internal <c>9092</c> listener.
    /// </remarks>
    private static string ResolveBootstrapServers(KafkaContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return $"{container.Hostname}:{container.GetMappedPublicPort(KafkaBuilder.KafkaPort)}";
    }
}
