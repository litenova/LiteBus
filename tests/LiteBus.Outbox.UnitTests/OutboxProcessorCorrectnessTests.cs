using System.Diagnostics.Metrics;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies outbox processor outcome honesty and lease semantics.
/// </summary>
public sealed class OutboxProcessorCorrectnessTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidateOptions_when_heartbeat_exceeds_half_lease_duration_should_throw()
    {
        var options = new OutboxProcessorOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(10),
            LeaseHeartbeatInterval = TimeSpan.FromSeconds(6)
        };

        var act = () => OutboxProcessorFactory.ValidateOptions(options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*half of the lease duration*");
    }

    [Fact]
    public async Task PipelinedProcessor_when_lease_renewal_fails_should_cancel_dispatch()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = new LeaseFailingOutboxStore(new InMemoryOutboxStore(timeProvider: clock));
        var dispatchCount = 0;
        var dispatcher = new CountingOutboxDispatcher(() => Interlocked.Increment(ref dispatchCount));

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            dispatcher,
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "lease-lost-worker",
                LeaseDuration = TimeSpan.FromSeconds(10),
                LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(50),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            clock,
            Array.Empty<IProcessorEnvelopeHook>());

        var messageId = Guid.NewGuid();
        await store.Inner.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        var result = await processor.ProcessPendingAsync();

        result.SucceededCount.Should().Be(0);
        dispatchCount.Should().BeLessThan(2);
        store.Inner.Get(messageId).Status.Should().Be(OutboxStatus.Publishing);
    }

    [Fact]
    public async Task PipelinedProcessor_when_lease_renewal_fails_should_increment_lease_lost_metric()
    {
        long measurementCount = 0;
        MeterListener? meterListener = null;

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == LiteBusOutboxTelemetry.MeterName &&
                    instrument.Name == LiteBusOutboxTelemetry.ProcessorLeaseLostInstrumentName)
                {
                    meterListener!.EnableMeasurementEvents(instrument);
                }
            }
        };

        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            measurementCount += measurement;
        });

        meterListener.Start();

        var clock = new ManualTimeProvider(BaseTime);
        var store = new LeaseFailingOutboxStore(new InMemoryOutboxStore(timeProvider: clock));
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CountingOutboxDispatcher(() => { }),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "lease-lost-worker",
                LeaseDuration = TimeSpan.FromSeconds(10),
                LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(50),
                DispatcherConcurrency = 1
            },
            clock,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        await processor.ProcessPendingAsync();

        measurementCount.Should().BeGreaterThan(0);
        meterListener.Dispose();
    }

    [Fact]
    public async Task PipelinedProcessor_when_after_dispatch_hook_fails_should_not_redispatch()
    {
        var store = new InMemoryOutboxStore();
        var dispatchCount = 0;
        var dispatcher = new CountingOutboxDispatcher(() => Interlocked.Increment(ref dispatchCount));

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            dispatcher,
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "hook-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            [new ThrowingAfterDispatchHook()]);

        var messageId = Guid.NewGuid();
        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        var result = await processor.ProcessPendingAsync();

        dispatchCount.Should().Be(1);
        result.DeadLetteredCount.Should().Be(1);
        store.Get(messageId).Status.Should().Be(OutboxStatus.DeadLettered);
    }

    [Fact]
    public async Task PipelinedProcessor_when_after_dispatch_hook_fails_should_persist_dead_letter_without_published()
    {
        var store = new PersistRecordingOutboxStore(new InMemoryOutboxStore());
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CountingOutboxDispatcher(() => { }),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "hook-persist-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            [new ThrowingAfterDispatchHook()]);

        var messageId = Guid.NewGuid();
        await store.Inner.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        await processor.ProcessPendingAsync();

        store.PersistedStatuses.Should().Equal(OutboxStatus.DeadLettered);
        store.Inner.Get(messageId).Status.Should().Be(OutboxStatus.DeadLettered);
    }

    [Fact]
    public async Task PipelinedProcessor_when_persist_skipped_should_increment_persist_skipped_metric()
    {
        long measurementCount = 0;
        MeterListener? meterListener = null;

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == LiteBusOutboxTelemetry.MeterName &&
                    instrument.Name == LiteBusOutboxTelemetry.ProcessorPersistSkippedInstrumentName)
                {
                    meterListener!.EnableMeasurementEvents(instrument);
                }
            }
        };

        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            measurementCount += measurement;
        });

        meterListener.Start();

        var store = new SkippingPersistOutboxStore(new InMemoryOutboxStore());
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CountingOutboxDispatcher(() => { }),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-skip-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        await processor.ProcessPendingAsync();

        measurementCount.Should().Be(1);
        meterListener.Dispose();
    }

    [Fact]
    public async Task PipelinedProcessor_default_options_should_pass_none_to_terminal_persist()
    {
        var store = new TokenCapturingOutboxStore(new InMemoryOutboxStore());
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CountingOutboxDispatcher(() => { }),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-token-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                HonorShutdownTokenOnPersist = false
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        using var cts = new CancellationTokenSource();
        await processor.ProcessPendingAsync(cts.Token);

        store.LastPersistToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task PipelinedProcessor_when_honor_shutdown_enabled_should_pass_dispatch_token_to_persist()
    {
        var store = new TokenCapturingOutboxStore(new InMemoryOutboxStore());
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CountingOutboxDispatcher(() => { }),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-token-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                HonorShutdownTokenOnPersist = true
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        using var cts = new CancellationTokenSource();
        await processor.ProcessPendingAsync(cts.Token);

        store.LastPersistToken.Should().Be(cts.Token);
    }

    private sealed class CountingOutboxDispatcher : IOutboxDispatcher
    {
        private readonly Action _onDispatch;

        public CountingOutboxDispatcher(Action onDispatch)
        {
            _onDispatch = onDispatch;
        }

        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _onDispatch();
            return Task.Delay(200, cancellationToken);
        }
    }

    private sealed class ThrowingAfterDispatchHook : IProcessorEnvelopeHook
    {
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("AfterDispatch failed.");
    }

    private sealed class LeaseFailingOutboxStore : IOutboxProcessingStore
    {
        private int _renewalAttempts;

        public LeaseFailingOutboxStore(InMemoryOutboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryOutboxStore Inner { get; }

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            Inner.LeasePendingAsync(request, cancellationToken);

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            _renewalAttempts++;
            return Task.FromResult(_renewalAttempts == 1);
        }

        public Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            Inner.PersistAsync(envelopes, cancellationToken);
    }

    private sealed class SkippingPersistOutboxStore : IOutboxProcessingStore
    {
        public SkippingPersistOutboxStore(InMemoryOutboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryOutboxStore Inner { get; }

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            Inner.LeasePendingAsync(request, cancellationToken);

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            Inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);

        public Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistResult.FromOutcome(0, envelopes.Count));
    }

    private sealed class PersistRecordingOutboxStore : IOutboxProcessingStore
    {
        public PersistRecordingOutboxStore(InMemoryOutboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryOutboxStore Inner { get; }

        public List<OutboxStatus> PersistedStatuses { get; } = [];

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            Inner.LeasePendingAsync(request, cancellationToken);

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            Inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);

        public async Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            foreach (var envelope in envelopes)
            {
                PersistedStatuses.Add(envelope.Status);
            }

            return await Inner.PersistAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TokenCapturingOutboxStore : IOutboxProcessingStore
    {
        public TokenCapturingOutboxStore(InMemoryOutboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryOutboxStore Inner { get; }

        public CancellationToken LastPersistToken { get; private set; }

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            Inner.LeasePendingAsync(request, cancellationToken);

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            Inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);

        public async Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            LastPersistToken = cancellationToken;
            return await Inner.PersistAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }
    }
}
