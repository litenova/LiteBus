using LiteBus.Amqp;

namespace LiteBus.Inbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     Inbox AMQP dispatch integration tests against RabbitMQ.
/// </summary>
public sealed class RabbitMqInboxDispatchIntegrationTests : AmqpInboxDispatcherIntegrationTests, IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RabbitMqInboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared RabbitMQ container fixture.</param>
    public RabbitMqInboxDispatchIntegrationTests(RabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override AmqpConnectionOptions ConnectionOptions => _fixture.ConnectionOptions;
}
