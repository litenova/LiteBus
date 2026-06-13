using LiteBus.Transport.IntegrationTesting.Kafka;

namespace LiteBus.Outbox.Dispatch.Kafka.IntegrationTests;

/// <summary>
///     Registers the shared Kafka broker collection fixture for outbox dispatch integration tests.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaIntegrationTestCollection : ICollectionFixture<KafkaBrokerFixture>;
