using LiteBus.Inbox.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox retention cleanup drives delays through the injected time provider.
/// </summary>
public sealed class InboxCleanupTimeProviderTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies the first retry after a transient failure waits five seconds on the injected clock.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_when_store_fails_should_retry_after_five_seconds_of_provider_time()
    {
        var clock = new StepTimeProvider(BaseTime);
        var options = new InboxCleanupHostOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromHours(1),
            Retention = TimeSpan.FromHours(1)
        };
        var coordinator = new InboxRetentionCoordinator(options);
        var store = new ThrowingRetentionStore();
        var cleanup = new InboxCleanupBackgroundService(store, options, clock, coordinator, NullLogger<InboxCleanupBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var execute = cleanup.ExecuteAsync(cts.Token);

        await WaitForConditionAsync(() => store.CallCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await WaitForConditionAsync(() => coordinator.GetStatus().LastError is not null, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None).ConfigureAwait(false);
        store.CallCount.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None).ConfigureAwait(false);
        store.CallCount.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(() => store.CallCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        store.CallCount.Should().Be(2);

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

    private sealed class ThrowingRetentionStore : IInboxRetentionStore
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromException<int>(new IOException("retention store unavailable"));
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
