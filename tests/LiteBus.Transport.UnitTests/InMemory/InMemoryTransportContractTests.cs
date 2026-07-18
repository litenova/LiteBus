using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using LiteBus.Transport.Testing;

namespace LiteBus.Transport.UnitTests.InMemory;

/// <summary>
///     Runs the shared transport conformance suite against the in-memory adapter.
/// </summary>
public sealed class InMemoryTransportContractTests : TransportContractTests
{
    /// <inheritdoc />
    protected override ValueTask<TransportContractContext> CreateContextAsync(string scenario)
    {
        var broker = new InMemoryTransportBroker();
        var consumer = new InMemoryConsumer(broker);
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var destination = $"litebus-{scenario}-{Guid.NewGuid():N}";

        return ValueTask.FromResult(new TransportContractContext(
            publisher,
            consumer,
            new TransportConsumerOptions { Destination = destination },
            destination,
            consumer.DisposeAsync));
    }
}
