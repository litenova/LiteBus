using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;
using LiteBus.Transport.IntegrationTesting.Azure;
using LiteBus.Transport.Testing;

namespace LiteBus.Transport.IntegrationTests.Azure;

/// <summary>
///     Runs the shared transport conformance suite against Azure Service Bus.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusTransportContractTests : TransportContractTests
{
    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusTransportContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusTransportContractTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Azure Service Bus emulator");

        if (!_fixture.IsAvailable)
        {
            throw new InvalidOperationException(DockerTestGate.DockerRequiredMessage);
        }
    }

    /// <inheritdoc />
    protected override ValueTask<TransportContractContext> CreateContextAsync(string scenario)
    {
        var queueName = _fixture.ResolveQueue($"contract-{scenario}");
        var client = new ServiceBusClient(_fixture.TransportOptions.ConnectionString);
        var publisher = new AzureServiceBusPublisher(client, new TransportCircuitBreakerRegistry());
        var consumer = new AzureServiceBusConsumer(client, _fixture.TransportOptions);

        return ValueTask.FromResult(new TransportContractContext(
            publisher,
            consumer,
            new TransportConsumerOptions { Destination = queueName },
            queueName,
            async () =>
            {
                await consumer.DisposeAsync().ConfigureAwait(false);
                await publisher.DisposeAsync().ConfigureAwait(false);
                await client.DisposeAsync().ConfigureAwait(false);
            }));
    }
}
