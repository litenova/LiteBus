using LiteBus.Transport.IntegrationTesting.Kafka;
using LiteBus.Transport.Kafka;

namespace LiteBus.Inbox.Ingress.Kafka.IntegrationTests;

/// <summary>
///     xUnit collection fixture that shares one Kafka container for ingress integration tests.
/// </summary>
public sealed class KafkaBrokerFixture : IAsyncLifetime
{
    private readonly KafkaBrokerHost _host = new();

    /// <summary>
    ///     Gets the transport options for the started Kafka container.
    /// </summary>
    public KafkaTransportOptions TransportOptions => _host.TransportOptions;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _host.StartAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _host.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
///     Serializes Kafka inbox ingress integration tests that share one broker container.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaBrokerCollection : ICollectionFixture<KafkaBrokerFixture>
{
    /// <summary>
    ///     The shared xUnit collection name.
    /// </summary>
    public const string Name = "Kafka.Inbox.Ingress";
}
