using LiteBus.Transport.Abstractions;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Transport.Testing;

namespace LiteBus.Transport.IntegrationTests.Amqp;

/// <summary>
///     Runs the shared transport conformance suite against RabbitMQ.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
public sealed class RabbitMqTransportContractTests : TransportContractTests, IClassFixture<RabbitMqFixture>
{
    /// <summary>
    ///     Gets the RabbitMQ fixture that owns the Testcontainers broker instance.
    /// </summary>
    private readonly RabbitMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RabbitMqTransportContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The RabbitMQ fixture started for this test class.</param>
    public RabbitMqTransportContractTests(RabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override ValueTask<TransportContractContext> CreateContextAsync(string scenario)
    {
        var manager = new AmqpConnectionManager(_fixture.ConnectionOptions);
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());
        var consumer = new AmqpConsumer(manager);
        var queueName = $"litebus-contract-{scenario}-{Guid.NewGuid():N}";

        return ValueTask.FromResult(new TransportContractContext(
            publisher,
            consumer,
            new TransportConsumerOptions { Destination = queueName },
            string.Empty,
            async () =>
            {
                await consumer.DisposeAsync().ConfigureAwait(false);
                await publisher.DisposeAsync().ConfigureAwait(false);
                await manager.DisposeAsync().ConfigureAwait(false);
            },
            queueName));
    }
}
