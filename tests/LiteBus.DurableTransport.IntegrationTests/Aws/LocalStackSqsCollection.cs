using Xunit;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Serializes LocalStack SQS durable transport tests that share one container fixture.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LocalStackSqsCollection : ICollectionFixture<LocalStackSqsFixture>
{
    /// <summary>
    ///     The shared xUnit collection name.
    /// </summary>
    public const string Name = "LocalStack SQS durable transport";
}
