namespace LiteBus.Transport.IntegrationTesting.Azure;

/// <summary>
///     xUnit collection that shares one Azure Service Bus emulator container across tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ServiceBusEmulatorCollection : ICollectionFixture<ServiceBusEmulatorFixture>
{
    /// <summary>
    ///     Gets the collection name used by Azure Service Bus emulator integration tests.
    /// </summary>
    public const string Name = "ServiceBusEmulator";
}