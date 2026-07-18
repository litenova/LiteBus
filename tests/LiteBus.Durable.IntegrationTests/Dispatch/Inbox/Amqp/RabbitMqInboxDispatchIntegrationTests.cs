using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Amqp;

/// <summary>
///     Inbox AMQP dispatch integration tests against RabbitMQ.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
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