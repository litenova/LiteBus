using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies AMQP connectivity diagnostics map connection failures to unhealthy results.
/// </summary>
public sealed class AmqpConnectivityDiagnosticCheckTests
{
    /// <summary>
    ///     Verifies cancellation is returned as an unhealthy diagnostic result instead of escaping the probe.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CheckAsync_WhenConnectionIsCanceled_ShouldReturnUnhealthyResult()
    {
        var check = new AmqpConnectivityDiagnosticCheck(
            new ThrowingConnectionManager(new OperationCanceledException("canceled")));

        var result = await check.CheckAsync().ConfigureAwait(false);

        check.Name.Should().Be("transport.amqp.connectivity");
        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
        result.Description.Should().Contain("canceled");
        result.Data.Should().NotBeNull();
        result.Data!["errorType"].Should().Be(nameof(OperationCanceledException));
    }

    /// <summary>
    ///     Verifies unexpected connection failures are returned as unhealthy diagnostic results.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CheckAsync_WhenConnectionFailsUnexpectedly_ShouldReturnUnhealthyResult()
    {
        var check = new AmqpConnectivityDiagnosticCheck(
            new ThrowingConnectionManager(new InvalidOperationException("unexpected")));

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
        result.Description.Should().Contain("unexpected");
        result.Data.Should().NotBeNull();
        result.Data!["errorType"].Should().Be(nameof(InvalidOperationException));
    }

    private sealed class ThrowingConnectionManager : IAmqpConnectionManager
    {
        private readonly Exception _exception;

        public ThrowingConnectionManager(Exception exception)
        {
            _exception = exception;
        }

        public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<IConnection>(_exception);
        }

        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
