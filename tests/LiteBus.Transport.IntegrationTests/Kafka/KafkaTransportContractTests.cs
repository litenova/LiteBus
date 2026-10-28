using Confluent.Kafka;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.IntegrationTesting.Kafka;
using LiteBus.Transport.Kafka;
using LiteBus.Transport.Testing;

namespace LiteBus.Transport.IntegrationTests.Kafka;

/// <summary>
///     Runs the shared transport conformance suite against Kafka.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class KafkaTransportContractTests : TransportContractTests
{
    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaTransportContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaTransportContractTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Kafka");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <inheritdoc />
    protected override async ValueTask<TransportContractContext> CreateContextAsync(string scenario)
    {
        var topic = KafkaTransportTestInfrastructure.CreateTopic($"contract-{scenario}");

        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(
            _fixture.TransportOptions.BootstrapServers,
            topic).ConfigureAwait(false);

        var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = _fixture.TransportOptions.BootstrapServers
        }).Build();

        var consumerClient = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = _fixture.TransportOptions.BootstrapServers,
            GroupId = $"litebus-contract-{scenario}-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

        var publisher = new KafkaPublisher(producer, new TransportCircuitBreakerRegistry());
        var consumer = new KafkaConsumer(consumerClient, _fixture.TransportOptions);

        return new TransportContractContext(
            publisher,
            consumer,
            new TransportConsumerOptions { Destination = topic },
            topic,
            async () =>
            {
                // The consumer owns its client, so disposing the consumer releases it.
                await consumer.DisposeAsync().ConfigureAwait(false);
                producer.Dispose();
            });
    }
}
