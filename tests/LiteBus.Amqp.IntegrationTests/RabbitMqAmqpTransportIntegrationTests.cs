using LiteBus.Amqp;

namespace LiteBus.Amqp.IntegrationTests;

/// <summary>
///     Runs shared AMQP transport tests against RabbitMQ.
/// </summary>
public sealed class RabbitMqAmqpTransportIntegrationTests : AmqpTransportIntegrationTests, IClassFixture<RabbitMqFixture>
{
    /// <summary>
    ///     Gets the RabbitMQ fixture that owns the Testcontainers broker instance.
    /// </summary>
    private readonly RabbitMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RabbitMqAmqpTransportIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The RabbitMQ fixture started for this test class.</param>
    public RabbitMqAmqpTransportIntegrationTests(RabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override AmqpConnectionOptions ConnectionOptions => _fixture.ConnectionOptions;

    /// <inheritdoc />
    protected override string BrokerName => "RabbitMQ";
}
