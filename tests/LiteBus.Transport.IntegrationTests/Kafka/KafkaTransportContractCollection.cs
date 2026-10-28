using LiteBus.Transport.IntegrationTesting.Kafka;

namespace LiteBus.Transport.IntegrationTests.Kafka;

/// <summary>
///     Registers the shared Kafka broker collection fixture for transport contract tests.
/// </summary>
[CollectionDefinition(KafkaBrokerCollection.Name)]
public sealed class KafkaTransportContractCollection : ICollectionFixture<KafkaBrokerFixture>;
