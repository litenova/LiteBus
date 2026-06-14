namespace LiteBus.Durable.IntegrationTests.Ingress.Amqp;

/// <summary>
///     Shares one RabbitMQ container across durable transport AMQP integration tests.
/// </summary>
[CollectionDefinition(RabbitMqBrokerFixture.CollectionName)]
public sealed class RabbitMqBrokerCollection : ICollectionFixture<RabbitMqBrokerFixture>
{
    /// <summary>
    ///     Gets the xUnit collection name.
    /// </summary>
    public const string Name = RabbitMqBrokerFixture.CollectionName;
}
