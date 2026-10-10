using LiteBus.Transport.IntegrationTesting.Aws;

namespace LiteBus.Transport.IntegrationTests.Aws;

/// <summary>
///     Registers the shared LocalStack SQS collection fixture for transport contract tests.
/// </summary>
[CollectionDefinition(LocalStackSqsCollection.Name)]
public sealed class SqsTransportContractCollection : ICollectionFixture<LocalStackSqsFixture>;
