using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Hosting;

namespace LiteBus.Runtime.UnitTests;

/// <summary>
///     Verifies the generic-host adapter for one LiteBus background service.
/// </summary>
public sealed class BackgroundServiceHostWrapperTests
{
    /// <summary>
    ///     Verifies construction rejects a missing background service.
    /// </summary>
    [Fact]
    public void Constructor_WithNullService_ShouldThrow()
    {
        var act = () => new BackgroundServiceHostWrapper(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies stop is safe before the wrapper has started.
    /// </summary>
    [Fact]
    public async Task StopAsync_BeforeStart_ShouldComplete()
    {
        var wrapper = new BackgroundServiceHostWrapper(new RecordingBackgroundService());

        await wrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies host shutdown cancels and awaits the wrapped execution loop.
    /// </summary>
    [Fact]
    public async Task StartAndStopAsync_ShouldCancelAndAwaitService()
    {
        var service = new RecordingBackgroundService();
        var wrapper = new BackgroundServiceHostWrapper(service);

        await wrapper.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await wrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);

        service.ExecutionCompleted.Should().BeTrue();
        service.ObservedToken.IsCancellationRequested.Should().BeTrue();
    }

    private sealed class RecordingBackgroundService : IBackgroundService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        public bool ExecutionCompleted { get; private set; }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ObservedToken = stoppingToken;
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ExecutionCompleted = true;
            }
        }
    }
}
