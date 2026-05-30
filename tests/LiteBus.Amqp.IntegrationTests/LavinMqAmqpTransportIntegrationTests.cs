using LiteBus.Amqp;

namespace LiteBus.Amqp.IntegrationTests;

/// <summary>
///     Runs shared AMQP transport tests against LavinMQ.
/// </summary>
public sealed class LavinMqAmqpTransportIntegrationTests : AmqpTransportIntegrationTests, IClassFixture<LavinMqFixture>
{
    /// <summary>
    ///     Gets the LavinMQ fixture that owns the Testcontainers broker instance.
    /// </summary>
    private readonly LavinMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LavinMqAmqpTransportIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The LavinMQ fixture started for this test class.</param>
    public LavinMqAmqpTransportIntegrationTests(LavinMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override AmqpConnectionOptions ConnectionOptions => _fixture.ConnectionOptions;

    /// <inheritdoc />
    protected override string BrokerName => "LavinMQ";
}
