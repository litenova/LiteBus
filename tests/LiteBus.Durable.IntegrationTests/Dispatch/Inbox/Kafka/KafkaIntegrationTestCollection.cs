using LiteBus.Transport.IntegrationTesting.Kafka;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Kafka;

/// <summary>
///     Registers the shared Kafka broker collection fixture for inbox dispatch integration tests.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaIntegrationTestCollection : ICollectionFixture<KafkaBrokerFixture>;
