using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Transport.Kafka;
using Testcontainers.Kafka;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     Shared Kafka container fixture for durable transport integration tests.
/// </summary>
public sealed class KafkaBrokerFixture : IAsyncLifetime
{
    /// <summary>
    ///     Gets the transport options for the started Kafka container.
    /// </summary>
    public KafkaTransportOptions TransportOptions { get; private set; } = null!;

    /// <summary>
    ///     The running Kafka test container.
    /// </summary>
    private KafkaContainer? _container;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await DockerTestGate.RunAsync(async () =>
        {
            _container = new KafkaBuilder().Build();
            await _container.StartAsync().ConfigureAwait(false);

            TransportOptions = new KafkaTransportOptions
            {
                BootstrapServers = _container.GetBootstrapAddress(),
                ConsumerGroupId = $"litebus-test-{Guid.NewGuid():N}",
                ClientId = "litebus-durable-transport-integration-tests"
            };
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
