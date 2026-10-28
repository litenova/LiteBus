using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Disposal;

/// <summary>
///     Verifies AMQP connection manager disposal is idempotent without a broker connection.
/// </summary>
public sealed class AmqpConnectionManagerDisposalTests
{
    /// <summary>
    ///     Verifies disposing a never-connected manager twice completes without throwing.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldCompleteWithoutThrowing()
    {
        var manager = new AmqpConnectionManager(new AmqpConnectionOptions());

        await manager.DisposeAsync().ConfigureAwait(false);

        // The second disposal must observe the disposed guard instead of the disposed gate.
        await manager.DisposeAsync().ConfigureAwait(false);
    }
}
