using LiteBus.Transport.IntegrationTesting.Kafka;

namespace LiteBus.Inbox.Ingress.Kafka.IntegrationTests;

/// <summary>
///     Registers the shared Kafka broker collection fixture for ingress integration tests.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaIntegrationTestCollection : ICollectionFixture<KafkaBrokerFixture>;
