using System.Collections.Concurrent;
using System.Reflection;
using Azure.Messaging.ServiceBus;
using LiteBus.Transport;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Transport.UnitTests.AzureServiceBus;

/// <summary>
///     Verifies Azure Service Bus publisher synchronous disposal swallows shutdown cancellations.
/// </summary>
public sealed class AzureServiceBusPublisherDisposeTests
{
    /// <summary>
    ///     Verifies a cancellation during sender close does not escape synchronous disposal.
    /// </summary>
    /// <returns>A task that completes when disposal has been verified.</returns>
    [Fact]
    public async Task Dispose_WhenSenderCloseIsCanceled_ShouldCompleteWithoutThrowing()
    {
        const string connectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key";
        var client = new ServiceBusClient(connectionString);
        await using (client.ConfigureAwait(false))
        {
            var publisher = new AzureServiceBusPublisher(client, new TransportCircuitBreakerRegistry());
            var senders = (ConcurrentDictionary<string, ServiceBusSender>) typeof(AzureServiceBusPublisher)
                .GetField("_senders", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(publisher)!;
            senders["orders"] = new CancelledSender();

            var act = () => publisher.Dispose();

            act.Should().NotThrow();
        }
    }

    private sealed class CancelledSender : ServiceBusSender
    {
#pragma warning disable CA2215 // The double simulates a close failure; its mocking base has no connection to close.
        public override async ValueTask DisposeAsync()
#pragma warning restore CA2215
        {
            await Task.CompletedTask.ConfigureAwait(false);
            throw new OperationCanceledException("Simulated cancellation during shutdown.");
        }
    }
}
