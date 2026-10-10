using LiteBus.Transport.IntegrationTesting.Azure;

namespace LiteBus.Transport.IntegrationTests.Azure;

/// <summary>
///     Registers the shared Service Bus emulator collection fixture for transport contract tests.
/// </summary>
[CollectionDefinition(ServiceBusEmulatorCollection.Name)]
public sealed class AzureServiceBusTransportContractCollection : ICollectionFixture<ServiceBusEmulatorFixture>;
