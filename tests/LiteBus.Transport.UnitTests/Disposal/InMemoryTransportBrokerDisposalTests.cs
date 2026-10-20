using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.Disposal;

/// <summary>
///     Verifies in-memory broker disposal completes channels and is idempotent.
/// </summary>
public sealed class InMemoryTransportBrokerDisposalTests
{
    /// <summary>
    ///     Verifies disposing twice completes destination channels without throwing.
    /// </summary>
    [Fact]
    public async Task Dispose_CalledTwice_ShouldCompleteChannelsWithoutThrowing()
    {
        var broker = new InMemoryTransportBroker();
        var endpoint = broker.GetOrCreateEndpoint("orders");

        broker.Dispose();

        // Disposal must complete the channel writer so pending readers observe completion.
        await endpoint.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        endpoint.Reader.Completion.IsCompletedSuccessfully.Should().BeTrue();

        broker.Dispose();
    }

    /// <summary>
    ///     Verifies reset still clears destinations after disposal for test isolation.
    /// </summary>
    [Fact]
    public void Reset_AfterDispose_ShouldAcceptNewDestinations()
    {
        var broker = new InMemoryTransportBroker();
        broker.GetOrCreateEndpoint("orders");
        broker.Dispose();

        broker.Reset();

        broker.GetOrCreateEndpoint("orders").Should().NotBeNull();
    }
}
