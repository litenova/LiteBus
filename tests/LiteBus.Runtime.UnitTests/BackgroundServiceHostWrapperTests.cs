using System.Reflection;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    ///     Verifies a loop that ignores cancellation is abandoned with a warning when the stop budget expires.
    /// </summary>
    [Fact]
    public async Task StopAsync_WhenLoopIgnoresCancellation_ShouldLogWarningAndReturn()
    {
        var service = new CancellationIgnoringBackgroundService();
        var logger = new RecordingLogger<BackgroundServiceHostWrapper>();
        var wrapper = new BackgroundServiceHostWrapper(service, logger);

        await wrapper.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var stoppingCts = GetStoppingCts(wrapper);

        using var stopBudget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await wrapper.StopAsync(stopBudget.Token).ConfigureAwait(false);

        logger.Warnings.Should().ContainSingle().Which.Should().Contain(nameof(CancellationIgnoringBackgroundService));
        var disposeCheck = () => stoppingCts.Cancel();
        disposeCheck.Should().Throw<ObjectDisposedException>();
        service.Release();
    }

    /// <summary>
    ///     Verifies a genuine loop fault still propagates from stop while the linked source is disposed.
    /// </summary>
    [Fact]
    public async Task StopAsync_WhenLoopFaults_ShouldPropagateAndDisposeLinkedSource()
    {
        var wrapper = new BackgroundServiceHostWrapper(new FaultingBackgroundService());

        await wrapper.StartAsync(CancellationToken.None).ConfigureAwait(false);
        var stoppingCts = GetStoppingCts(wrapper);

        var act = () => wrapper.StopAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        var disposeCheck = () => stoppingCts.Cancel();
        disposeCheck.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    ///     Verifies a loop that propagates shutdown cancellation stops cleanly.
    /// </summary>
    [Fact]
    public async Task StopAsync_WhenLoopPropagatesShutdownCancellation_ShouldComplete()
    {
        var service = new PropagatingCancellationBackgroundService();
        var wrapper = new BackgroundServiceHostWrapper(service);

        await wrapper.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var stoppingCts = GetStoppingCts(wrapper);

        await wrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);

        var disposeCheck = () => stoppingCts.Cancel();
        disposeCheck.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    ///     Verifies cancellation from an unrelated token reaches the orchestrator as a loop fault.
    /// </summary>
    [Fact]
    public async Task Orchestrator_WhenLoopThrowsUnrelatedCancellation_ShouldFailClosed()
    {
        using var lifetime = new RecordingHostApplicationLifetime();
        using var orchestrator = new LiteBusHostOrchestrator([], [new UnrelatedCancellationBackgroundService()], lifetime);

        await orchestrator.StartAsync(CancellationToken.None).ConfigureAwait(false);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (lifetime.StopApplicationCallCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        await orchestrator.StopAsync(CancellationToken.None).ConfigureAwait(false);

        lifetime.StopApplicationCallCount.Should().Be(1);
        orchestrator.ExecuteTask!.IsCompletedSuccessfully.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies stop can run twice without surfacing disposal of the linked source.
    /// </summary>
    [Fact]
    public async Task StopAsync_CalledTwice_ShouldComplete()
    {
        var service = new RecordingBackgroundService();
        var wrapper = new BackgroundServiceHostWrapper(service);

        await wrapper.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await wrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await wrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);

        service.ExecutionCompleted.Should().BeTrue();
    }

    private static CancellationTokenSource GetStoppingCts(BackgroundServiceHostWrapper wrapper)
    {
        var field = typeof(BackgroundServiceHostWrapper).GetField(
                        "_stoppingCts",
                        BindingFlags.NonPublic | BindingFlags.Instance) ??
                    throw new InvalidOperationException("_stoppingCts not found.");

        return (CancellationTokenSource) field.GetValue(wrapper)!;
    }

    private sealed class CancellationIgnoringBackgroundService : IBackgroundService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await _release.Task.ConfigureAwait(false);
            }
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class FaultingBackgroundService : IBackgroundService
    {
        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            throw new InvalidOperationException("Loop fault.");
        }
    }

    private sealed class PropagatingCancellationBackgroundService : IBackgroundService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
    }

    private sealed class UnrelatedCancellationBackgroundService : IBackgroundService
    {
        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var unrelated = new CancellationTokenSource();
            unrelated.Cancel();

            throw new OperationCanceledException(unrelated.Token);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
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
