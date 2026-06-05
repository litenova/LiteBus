using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class PipelinedInboxProcessorTests : LiteBusTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PipelinedProcessor_WithConcurrencyOne_ShouldMatchLegacyOutcomes()
    {
        var store = new InMemoryInboxStore();
        var legacyRecorder = new InboxTestFixtures.CommandRecorder();
        var pipelinedRecorder = new InboxTestFixtures.CommandRecorder();

        await using var legacyProvider = BuildProcessorProvider(
            new InMemoryInboxStore(),
            legacyRecorder,
            ProcessorArchitecture.Legacy,
            dispatcherConcurrency: 1);
        await using var pipelinedProvider = BuildProcessorProvider(
            store,
            pipelinedRecorder,
            ProcessorArchitecture.Pipelined,
            dispatcherConcurrency: 1);

        await SeedCommandsAsync(legacyProvider.GetRequiredService<IInbox>());
        await SeedCommandsAsync(pipelinedProvider.GetRequiredService<IInbox>());

        var legacyResult = await legacyProvider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync();
        var pipelinedResult = await pipelinedProvider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync();

        legacyResult.SucceededCount.Should().Be(3);
        pipelinedResult.SucceededCount.Should().Be(legacyResult.SucceededCount);
        pipelinedRecorder.Commands.Should().HaveCount(3);
        store.GetAll(InboxStatus.Completed).Should().HaveCount(3);
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
            Array.Empty<IInboxProcessorEnvelopeHook>());

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
        });

        var result = await processor.ProcessPendingAsync();

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
            Array.Empty<IInboxProcessorEnvelopeHook>());

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
            });
        }

        var passTask = processor.ProcessPendingAsync();
        await dispatcher.WaitForConcurrentDispatchAsync();
        gate.SetResult();
        var result = await passTask;

        result.SucceededCount.Should().Be(6);
        dispatcher.MaxConcurrent.Should().BeGreaterThan(1);
    }

    private static async Task SeedCommandsAsync(IInbox inbox)
    {
        for (var index = 0; index < 3; index++)
        {
            var orderId = Guid.NewGuid();
            await inbox.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            });
        }
    }

    private static ServiceProvider BuildProcessorProvider(
        InMemoryInboxStore store,
        InboxTestFixtures.CommandRecorder recorder,
        ProcessorArchitecture architecture,
        int dispatcherConcurrency)
    {
        var services = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                modules.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Architecture = architecture,
                        DispatcherConcurrency = dispatcherConcurrency,
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            });

        return services.BuildServiceProvider();
    }

    private sealed class RenewalCountingLeaseStore : IInboxProcessingStore
    {
        private readonly InMemoryInboxStore _inner;

        public RenewalCountingLeaseStore(InMemoryInboxStore inner)
        {
            _inner = inner;
        }

        public int RenewalCount { get; private set; }

        public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(envelope, cancellationToken);

        public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            _inner.AddBatchAsync(envelopes, cancellationToken);

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.LeasePendingAsync(request, cancellationToken);

        public async Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            RenewalCount++;
            return await _inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default) =>
            _inner.PersistAsync(envelopes, cancellationToken);
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

        public Task WaitForConcurrentDispatchAsync() => _concurrentReached.Task;

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
