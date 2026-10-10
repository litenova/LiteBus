using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests.Disposal;

/// <summary>
///     Verifies AMQP consumer disposal is idempotent without a broker connection.
/// </summary>
public sealed class AmqpConsumerDisposalTests
{
    /// <summary>
    ///     Verifies disposing a never-started consumer twice completes without throwing.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldCompleteWithoutThrowing()
    {
        var consumer = new AmqpConsumer(new NeverConnectedConnectionManager());

        await consumer.DisposeAsync().ConfigureAwait(false);

        // The second disposal must observe the disposed guard instead of the disposed gate.
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class NeverConnectedConnectionManager : IAmqpConnectionManager
    {
        public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Disposal tests must not open a broker connection.");
        }

        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Disposal tests must not open a broker connection.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
