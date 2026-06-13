using LiteBus.Transport.IntegrationTesting.Kafka;

namespace LiteBus.Inbox.Dispatch.Kafka.IntegrationTests;

/// <summary>
///     Registers the shared Kafka broker collection fixture for inbox dispatch integration tests.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaIntegrationTestCollection : ICollectionFixture<KafkaBrokerFixture>;
