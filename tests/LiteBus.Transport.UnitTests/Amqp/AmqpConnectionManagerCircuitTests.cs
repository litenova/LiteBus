using LiteBus.Transport.Amqp;
using LiteBus.Transport.Abstractions;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies a cancelled connection probe releases the circuit breaker permit.
/// </summary>
public sealed class AmqpConnectionManagerCircuitTests
{
    /// <summary>
    ///     Verifies the attempt after a cancelled half-open probe is admitted instead of rejected.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task GetConnectionAsync_AfterCancelledHalfOpenProbe_ShouldAdmitNextAttempt()
    {
        var attempts = 0;

        // The first attempt fails and opens the circuit; every later attempt waits until it is cancelled, which is what
        // a broker that accepts the socket but never completes the handshake looks like to the client.
        async Task<IConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("broker refused the connection");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }

        var manager = new AmqpConnectionManager(
            new AmqpConnectionOptions
            {
                CircuitBreaker = new AmqpCircuitBreakerOptions
                {
                    FailureThreshold = 1,
                    BreakDuration = TimeSpan.FromMilliseconds(50)
                }
            },
            ConnectAsync);

        await using (manager.ConfigureAwait(false))
        {
            var refused = () => manager.GetConnectionAsync(CancellationToken.None);
            await refused.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            // The open circuit rejects without calling the broker.
            var rejected = () => manager.GetConnectionAsync(CancellationToken.None);
            await rejected.Should().ThrowAsync<TransportCircuitBreakerOpenException>().ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

            using (var probeCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                var probe = () => manager.GetConnectionAsync(probeCancellation.Token);
                await probe.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

            using (var retryCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                // Without the probe-permit release this attempt throws TransportCircuitBreakerOpenException at once and
                // never reaches the connect step.
                var retry = () => manager.GetConnectionAsync(retryCancellation.Token);
                await retry.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
            }

            attempts.Should().Be(3);
        }
    }
}
