using Azure.Messaging.ServiceBus;
using LiteBus.Transport;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Transport.UnitTests.Disposal;

/// <summary>
///     Verifies Azure Service Bus publisher disposal is bounded and idempotent.
/// </summary>
public sealed class AzureServiceBusPublisherDisposalTests
{
    /// <summary>
    ///     Verifies repeated asynchronous and synchronous disposal complete without throwing.
    /// </summary>
    [Fact]
    public async Task Dispose_CalledRepeatedly_ShouldCompleteWithoutThrowing()
    {
        const string connectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key";
        var client = new ServiceBusClient(connectionString);
        await using var clientScope = client.ConfigureAwait(false);
        var publisher = new AzureServiceBusPublisher(client, new TransportCircuitBreakerRegistry());

        await publisher.DisposeAsync().ConfigureAwait(false);
        await publisher.DisposeAsync().ConfigureAwait(false);

        // The synchronous path must bound the close wait and swallow close errors.
        publisher.Dispose();
    }
}
