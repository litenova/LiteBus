using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.Disposal;

/// <summary>
///     Verifies in-memory consumer disposal is idempotent.
/// </summary>
public sealed class InMemoryConsumerDisposalTests
{
    /// <summary>
    ///     Verifies disposing a never-started consumer twice completes without throwing.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldCompleteWithoutThrowing()
    {
        var consumer = new InMemoryConsumer(new InMemoryTransportBroker());

        await consumer.DisposeAsync().ConfigureAwait(false);

        // The second disposal must observe the disposed guard instead of the disposed gate.
        await consumer.DisposeAsync().ConfigureAwait(false);
    }
}
