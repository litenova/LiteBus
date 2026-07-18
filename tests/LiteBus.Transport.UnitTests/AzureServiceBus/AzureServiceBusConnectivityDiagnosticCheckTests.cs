using Azure.Messaging.ServiceBus;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Transport.UnitTests.AzureServiceBus;

/// <summary>
///     Verifies Azure Service Bus diagnostic behavior that does not require a live namespace.
/// </summary>
public sealed class AzureServiceBusConnectivityDiagnosticCheckTests
{
    /// <summary>
    ///     Verifies an omitted entity target reports degraded instead of treating an unopened client as healthy.
    /// </summary>
    /// <returns>A task that completes when the diagnostic result is available.</returns>
    [Fact]
    public async Task CheckAsync_WithoutTarget_ShouldReturnDegraded()
    {
        var client = new ServiceBusClient(
            "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==");
        await using (client.ConfigureAwait(false))
        {
            var check = new AzureServiceBusConnectivityDiagnosticCheck(
                client,
                new AzureServiceBusTransportOptions
                {
                    ConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA=="
                });

            var result = await check.CheckAsync().ConfigureAwait(false);

            check.Name.Should().Be("transport.azure_service_bus.connectivity");
            result.Status.Should().Be(DiagnosticStatus.Degraded);
            result.Description.Should().Contain(nameof(AzureServiceBusTransportOptions.ConnectivityCheckTarget));
        }
    }
}
