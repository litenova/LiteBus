using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Amqp;

/// <summary>
///     Inbox AMQP dispatch integration tests against LavinMQ.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
public sealed class LavinMqInboxDispatchIntegrationTests : AmqpInboxDispatcherIntegrationTests, IClassFixture<LavinMqFixture>
{
    private readonly LavinMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LavinMqInboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LavinMQ container fixture.</param>
    public LavinMqInboxDispatchIntegrationTests(LavinMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override AmqpConnectionOptions ConnectionOptions => _fixture.ConnectionOptions;
}