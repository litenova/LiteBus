using LiteBus.Transport.IntegrationTesting.Kafka;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Outbox.Kafka;

/// <summary>
///     Registers the shared Kafka broker collection fixture for outbox dispatch integration tests.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaIntegrationTestCollection : ICollectionFixture<KafkaBrokerFixture>;
