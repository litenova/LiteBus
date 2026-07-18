using LiteBus.Transport.IntegrationTesting.Azure;

namespace LiteBus.Durable.IntegrationTests.Fixtures;

/// <summary>
///     Registers the shared Azure Service Bus emulator fixture for durable integration tests in this assembly.
/// </summary>
[CollectionDefinition(ServiceBusEmulatorCollection.Name)]
public sealed class DurableServiceBusEmulatorCollection : ICollectionFixture<ServiceBusEmulatorFixture>;
