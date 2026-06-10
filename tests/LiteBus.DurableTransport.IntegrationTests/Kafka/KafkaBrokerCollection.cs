using Xunit;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     Serializes Kafka-backed durable transport tests that share one broker container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class KafkaBrokerCollection : ICollectionFixture<KafkaBrokerFixture>
{
    /// <summary>
    ///     The shared xUnit collection name.
    /// </summary>
    public const string Name = "Kafka durable transport";
}
