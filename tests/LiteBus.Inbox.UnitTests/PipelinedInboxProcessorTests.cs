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
        var clock = new ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore(timeProvider: clock);
        var leaseStore = new RenewalCountingLeaseStore(store);
        var slowDispatcher = new SlowInboxDispatcher(TimeSpan.FromSeconds(3));

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

        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.SucceededCount.Should().Be(1);
        store.Get(commandId).Status.Should().Be(InboxStatus.Completed);
        leaseStore.RenewalCount.Should().BeGreaterThan(0);
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

        public RenewalCountingLeaseStore(InMemoryInboxStore inner)
        {
            _inner = inner;
        }

        public int RenewalCount { get; private set; }

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
            RenewalCount++;

            return await _inner.RenewLeaseAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public Task<PersistResult> PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            return _inner.PersistAsync(envelopes, cancellationToken);
        }
    }

    private sealed class SlowInboxDispatcher : IInboxDispatcher
    {
        private readonly TimeSpan _delay;

        public SlowInboxDispatcher(TimeSpan delay)
        {
            _delay = delay;
        }

        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
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
}
