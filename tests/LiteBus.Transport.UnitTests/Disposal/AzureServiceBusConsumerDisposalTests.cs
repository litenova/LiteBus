using Azure.Messaging.ServiceBus;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Transport.UnitTests.Disposal;

/// <summary>
///     Verifies Azure Service Bus consumer disposal is idempotent without a broker connection.
/// </summary>
public sealed class AzureServiceBusConsumerDisposalTests
{
    /// <summary>
    ///     Verifies disposing a never-started consumer twice completes without throwing.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldCompleteWithoutThrowing()
    {
        const string connectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key";
        var client = new ServiceBusClient(connectionString);
        await using var clientScope = client.ConfigureAwait(false);
        var consumer = new AzureServiceBusConsumer(
            client,
            new AzureServiceBusTransportOptions { ConnectionString = connectionString });

        await consumer.DisposeAsync().ConfigureAwait(false);

        // The second disposal must observe the disposed guard instead of the disposed gate.
        await consumer.DisposeAsync().ConfigureAwait(false);
    }
}
