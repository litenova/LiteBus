using LiteBus.Transport.IntegrationTesting.Aws;

namespace LiteBus.Durable.IntegrationTests.Fixtures;

/// <summary>
///     Registers the shared LocalStack SQS fixture for durable integration tests in this assembly.
/// </summary>
[CollectionDefinition(LocalStackSqsCollection.Name)]
public sealed class DurableLocalStackSqsCollection : ICollectionFixture<LocalStackSqsFixture>;
