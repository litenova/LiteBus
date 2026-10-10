using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Verifies processor loops drive delays through the injected time provider.
/// </summary>
public sealed class ProcessorTimeProviderTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies the heartbeat renews once per advanced interval.
    /// </summary>
    [Fact]
    public async Task RunWithHeartbeatAsync_when_time_advances_should_renew_once_per_interval()
    {
        var clock = new StepTimeProvider(BaseTime);
        var store = new CountingLeaseStore();
        var context = new LeaseHeartbeatContext(
            Guid.NewGuid(),
            "worker",
            1,
            store,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(1),
            clock,
            "test");

        var operation = ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
            context,
            async (heartbeatToken, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), clock, heartbeatToken).ConfigureAwait(false);
                return 42;
            },
            CancellationToken.None);

        await WaitForConditionAsync(() => store.RenewCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        store.RenewCount.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(() => store.RenewCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        store.RenewCount.Should().Be(2);

        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(() => store.RenewCount >= 3, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        store.RenewCount.Should().Be(3);

        clock.Advance(TimeSpan.FromSeconds(8));
        var result = await operation.ConfigureAwait(false);
        result.Should().Be(42);
    }

    /// <summary>
    ///     Verifies the startup delay elapses only when the injected clock advances.
    /// </summary>
    [Fact]
    public async Task ProcessorBackgroundService_when_startup_delay_uses_provider_should_wait_for_advance()
    {
        var clock = new StepTimeProvider(BaseTime);
        var processor = new CountingProcessor();
        var hostOptions = new ProcessorHostOptions
        {
            Enabled = true,
            StartupDelay = TimeSpan.FromHours(1),
            PollInterval = TimeSpan.FromHours(1),
            UseAdaptivePolling = false
        };
        var service = new ProcessorBackgroundService<CountingProcessor>(
            processor,
            new ProcessorOptions { BatchSize = 10 },
            hostOptions,
            new NoopWorkSignal(),
            new NoopControl(),
            static () => { },
            static (_, _) => { },
            NullLogger.Instance,
            clock);

        using var cts = new CancellationTokenSource();
        var execute = service.ExecuteAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None).ConfigureAwait(false);
        processor.CallCount.Should().Be(0);

        clock.Advance(TimeSpan.FromHours(1));
        await WaitForConditionAsync(() => processor.CallCount > 0, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        processor.CallCount.Should().BeGreaterThan(0);

        await cts.CancelAsync().ConfigureAwait(false);
        await execute.ConfigureAwait(false);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class CountingLeaseStore : ILeaseRenewable
    {
        private int _renewCount;

        public int RenewCount => _renewCount;

        public Task<bool> RenewLeaseAsync(LeaseRenewalRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _renewCount);
            return Task.FromResult(true);
        }
    }

    private sealed class CountingProcessor : IMessageProcessor
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ProcessorPassResult { LeasedCount = 0 });
        }
    }

    private sealed class NoopWorkSignal : IProcessorWorkSignal
    {
        public Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
        {
            // Never signals work, so the loop parks between passes until the test cancels it.
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopControl : IProcessorBackgroundControl
    {
        public CancellationToken DrainRequestedToken => CancellationToken.None;

        public bool IsDraining => false;

        public Task WaitIfPausedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void SignalPassComplete()
        {
        }

        public void SignalDrainComplete(Exception? exception)
        {
        }
    }

    private sealed class StepTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow;
        private readonly List<TimerEntry> _timers = [];

        public StepTimeProvider(DateTimeOffset initial)
        {
            _utcNow = initial;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public void Advance(TimeSpan amount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
            List<(TimerCallback Callback, object? State)> ready = [];

            lock (_sync)
            {
                _utcNow = _utcNow.Add(amount);

                foreach (var timer in _timers.ToArray())
                {
                    if (timer.Disposed || timer.Due is null || timer.Due > _utcNow)
                    {
                        continue;
                    }

                    if (timer.Period == Timeout.InfiniteTimeSpan || timer.Period <= TimeSpan.Zero)
                    {
                        timer.Disposed = true;
                        _timers.Remove(timer);
                        ready.Add((timer.Callback, timer.State));
                    }
                    else
                    {
                        timer.Due = timer.Due.Value.Add(timer.Period);
                        ready.Add((timer.Callback, timer.State));
                    }
                }
            }

            foreach (var (callback, state) in ready)
            {
                callback(state);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var entry = new TimerEntry(callback, state);

            lock (_sync)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    entry.Due = null;
                }
                else if (dueTime <= TimeSpan.Zero)
                {
                    entry.Due = _utcNow;
                }
                else
                {
                    entry.Due = _utcNow.Add(dueTime);
                }

                entry.Period = period;
                _timers.Add(entry);
            }

            return new StepTimer(this, entry);
        }

        private sealed class TimerEntry
        {
            public TimerEntry(TimerCallback callback, object? state)
            {
                Callback = callback;
                State = state;
            }

            public TimerCallback Callback { get; }

            public object? State { get; }

            public DateTimeOffset? Due { get; set; }

            public TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;

            public bool Disposed { get; set; }
        }

        private sealed class StepTimer : ITimer
        {
            private readonly StepTimeProvider _owner;
            private readonly TimerEntry _entry;
            private bool _disposed;

            public StepTimer(StepTimeProvider owner, TimerEntry entry)
            {
                _owner = owner;
                _entry = entry;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_owner._sync)
                {
                    if (_disposed || _entry.Disposed)
                    {
                        return false;
                    }

                    if (dueTime == Timeout.InfiniteTimeSpan)
                    {
                        _entry.Due = null;
                    }
                    else if (dueTime <= TimeSpan.Zero)
                    {
                        _entry.Due = _owner._utcNow;
                    }
                    else
                    {
                        _entry.Due = _owner._utcNow.Add(dueTime);
                    }

                    _entry.Period = period;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_owner._sync)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _entry.Disposed = true;
                    _owner._timers.Remove(_entry);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
