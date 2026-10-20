using System.Net;
using System.Net.Sockets;
using LiteBus.Transport.Amqp;
using RabbitMQ.Client.Exceptions;

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
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();

        var manager = new AmqpConnectionManager(new AmqpConnectionOptions
        {
            HostName = "127.0.0.1",
            Port = port,
            AutomaticRecoveryEnabled = false,
            CircuitBreaker = new AmqpCircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMilliseconds(100)
            }
        });
        await using (manager.ConfigureAwait(false))
        {
            try
            {
                var refused = () => manager.GetConnectionAsync(CancellationToken.None);
                await refused.Should().ThrowAsync<BrokerUnreachableException>().ConfigureAwait(false);

                // The listener accepts no connections, so the AMQP handshake hangs until cancelled.
                listener.Start();
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                using (var probeCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                {
                    var probe = () => manager.GetConnectionAsync(probeCancellation.Token);
                    await probe.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                using (var retryCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                {
                    // Without the probe-permit release this attempt throws
                    // TransportCircuitBreakerOpenException immediately instead of connecting.
                    var retry = () => manager.GetConnectionAsync(retryCancellation.Token);
                    await retry.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
                }
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
