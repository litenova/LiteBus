using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies a zero retry interval cannot turn a persistent failure into a hot restart loop.
/// </summary>
public sealed class IngressRestartDelayReleaseFixesTests
{
    /// <summary>
    ///     Verifies persistent startup failures restart no faster than the minimum restart delay.
    /// </summary>
    /// <returns>A task that completes when the pacing assertion succeeds.</returns>
    [Fact]
    public async Task ExecuteAsync_WhenRetryIntervalIsZeroAndStartAlwaysFails_ShouldPaceRestarts()
    {
        var transportConsumer = new AlwaysFailingConsumer();
        var consumer = CreateConsumer(transportConsumer);
        using var stoppingSource = new CancellationTokenSource();

        var execution = consumer.ExecuteAsync(stoppingSource.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(2200), CancellationToken.None).ConfigureAwait(false);
        await stoppingSource.CancelAsync().ConfigureAwait(false);
        await execution.ConfigureAwait(false);

        transportConsumer.StartCallCount.Should().BeGreaterThanOrEqualTo(2);
        transportConsumer.StartCallCount.Should().BeLessThanOrEqualTo(5);
    }

    /// <summary>
    ///     Creates an ingress consumer whose transport always fails to start.
    /// </summary>
    /// <param name="transportConsumer">The failing transport consumer.</param>
    /// <returns>The configured ingress consumer.</returns>
    private static TransportInboxIngressConsumer CreateConsumer(AlwaysFailingConsumer transportConsumer)
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command");

        var inbox = InboxWriterTestFactory.Create(
            new InMemoryInboxStore(),
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var options = new TransportInboxIngressOptions
        {
            Destination = "probe.destination",
            Safety = new TransportInboxIngressSafetyOptions
            {
                RequireStableIdentity = false
            }
        };

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            options);

        return new TransportInboxIngressConsumer(
            transportConsumer,
            handler,
            options,
            new TransportInboxIngressHostOptions
            {
                Enabled = true,
                RetryPollInterval = TimeSpan.Zero
            });
    }

    /// <summary>
    ///     Probe command type used by restart delay release tests.
    /// </summary>
    /// <param name="Value">The probe payload value.</param>
    private sealed record ProbeCommand(int Value);

    /// <summary>
    ///     Transport consumer that fails every start attempt.
    /// </summary>
    private sealed class AlwaysFailingConsumer : IMessageConsumer
    {
        /// <summary>
        ///     Gets the backing count of start attempts observed.
        /// </summary>
        private int _startCallCount;

        /// <summary>
        ///     Gets the number of start attempts observed.
        /// </summary>
        public int StartCallCount => Volatile.Read(ref _startCallCount);

        /// <inheritdoc />
        public Task StartAsync(
            TransportConsumerOptions options,
            Func<TransportMessage, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _startCallCount);
            return Task.FromException(new IOException("broker unavailable"));
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
