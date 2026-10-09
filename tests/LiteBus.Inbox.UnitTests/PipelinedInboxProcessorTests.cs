using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class PipelinedInboxProcessorTests : LiteBusTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PipelinedProcessor_WithConcurrencyOne_ShouldProcessAllCommands()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

         var provider = BuildProcessorProvider(recorder, 1);
         await using (provider.ConfigureAwait(false))
         {

        var store = provider.GetRequiredService<InMemoryInboxStore>();
        await SeedCommandsAsync(provider.GetRequiredService<IInbox>()).ConfigureAwait(false);

        var result = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync().ConfigureAwait(false);

        result.SucceededCount.Should().Be(3);
        recorder.Commands.Should().HaveCount(3);
        store.GetAll(InboxStatus.Completed).Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task PipelinedProcessor_WithHeartbeat_ShouldCompleteSlowHandlerWithoutReclaim()
    {
        var clock = new StepTimeProvider(BaseTime);
        var store = new InMemoryInboxStore(timeProvider: clock);
        var leaseStore = new RenewalCountingLeaseStore(store);
        var slowDispatcher = new SlowInboxDispatcher(TimeSpan.FromSeconds(3), clock);

        var processor = new PipelinedInboxProcessor(
            leaseStore,
            leaseStore,
            slowDispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "heartbeat-worker",
                LeaseDuration = TimeSpan.FromSeconds(1),
                LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(200),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            clock,
            Array.Empty<IProcessorEnvelopeHook>());

        var commandId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var passTask = processor.ProcessPendingAsync();

        await WaitForConditionAsync(() => leaseStore.RenewalCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        leaseStore.RenewalCount.Should().Be(1);

        clock.Advance(TimeSpan.FromMilliseconds(200));
        await WaitForConditionAsync(() => leaseStore.RenewalCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        leaseStore.RenewalCount.Should().Be(2);

        clock.Advance(TimeSpan.FromMilliseconds(200));
        await WaitForConditionAsync(() => leaseStore.RenewalCount >= 3, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        leaseStore.RenewalCount.Should().Be(3);

        clock.Advance(TimeSpan.FromSeconds(3));
        var result = await passTask.ConfigureAwait(false);

        result.SucceededCount.Should().Be(1);
        store.Get(commandId).Status.Should().Be(InboxStatus.Completed);
        leaseStore.RenewalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task PipelinedProcessor_WithParallelWorkers_ShouldDispatchConcurrently()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new ConcurrentTrackingInboxDispatcher(gate);
        var store = new InMemoryInboxStore();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 6,
                LeaseOwner = "parallel-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 3,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        for (var index = 0; index < 6; index++)
        {
            await store.AddAsync(new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "orders.commands.ship",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = BaseTime,
                AttemptCount = 0,
                Status = InboxStatus.Pending
            }).ConfigureAwait(false);
        }

        var passTask = processor.ProcessPendingAsync();
        await dispatcher.WaitForConcurrentDispatchAsync().ConfigureAwait(false);
        gate.SetResult();
        var result = await passTask.ConfigureAwait(false);

        result.SucceededCount.Should().Be(6);
        dispatcher.MaxConcurrent.Should().BeGreaterThan(1);
    }

    private static async Task SeedCommandsAsync(IInbox inbox)
    {
        for (var index = 0; index < 3; index++)
        {
            var orderId = Guid.NewGuid();

            await inbox.AcceptAsync(new InboxTestFixtures.ShipOrderCommand {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            }).ConfigureAwait(false);
        }
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

    private static ServiceProvider BuildProcessorProvider(
        InboxTestFixtures.CommandRecorder recorder,
        int dispatcherConcurrency)
    {
        return new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        DispatcherConcurrency = dispatcherConcurrency,
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();
    }

    private sealed class RenewalCountingLeaseStore : IInboxLeaseStore, IInboxStateWriter
    {
        private readonly InMemoryInboxStore _inner;
        private int _renewalCount;

        public RenewalCountingLeaseStore(InMemoryInboxStore inner)
        {
            _inner = inner;
        }

        public int RenewalCount => Volatile.Read(ref _renewalCount);

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return _inner.LeasePendingAsync(request, cancellationToken);
        }

        public async Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _renewalCount);

            return await _inner.RenewLeaseAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public Task<PersistResult> PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            return _inner.PersistAsync(envelopes, cancellationToken);
        }
    }

    private sealed class SlowInboxDispatcher : IInboxDispatcher
    {
        private readonly TimeProvider _clock;
        private readonly TimeSpan _delay;

        public SlowInboxDispatcher(TimeSpan delay, TimeProvider clock)
        {
            _delay = delay;
            _clock = clock;
        }

        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ConcurrentTrackingInboxDispatcher : IInboxDispatcher
    {
        private readonly TaskCompletionSource _concurrentReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate;
        private int _active;
        private int _maxConcurrent;

        public ConcurrentTrackingInboxDispatcher(TaskCompletionSource gate)
        {
            _gate = gate;
        }

        public int MaxConcurrent => _maxConcurrent;

        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(active);

            if (active >= 2)
            {
                _concurrentReached.TrySetResult();
            }

            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref _active);
        }

        public Task WaitForConcurrentDispatchAsync()
        {
            return _concurrentReached.Task;
        }

        private void UpdateMax(int active)
        {
            while (true)
            {
                var current = _maxConcurrent;

                if (active <= current || Interlocked.CompareExchange(ref _maxConcurrent, active, current) == current)
                {
                    return;
                }
            }
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
