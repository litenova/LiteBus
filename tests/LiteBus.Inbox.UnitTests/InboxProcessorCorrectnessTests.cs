using System.Diagnostics.Metrics;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox processor outcome honesty and lease semantics.
/// </summary>
public sealed class InboxProcessorCorrectnessTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidateOptions_when_heartbeat_exceeds_half_lease_duration_should_throw()
    {
        var options = new InboxProcessorOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(10),
            LeaseHeartbeatInterval = TimeSpan.FromSeconds(6)
        };

        var act = () => InboxProcessorFactory.ValidateOptions(options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*half of the lease duration*");
    }

    [Fact]
    public async Task ProcessAsync_with_pass_recorder_should_record_completed_outcome()
    {
        var accumulator = new ProcessorPassAccumulator<InboxEnvelope>();

        var envelope = new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 1,
            Status = InboxStatus.Processing
        };

        var updated = await InboxProcessorEnvelopeHandler.ProcessAsync(
            envelope,
            new CountingInboxDispatcher(() =>
            {
            }),
            new InboxProcessorOptions(),
            TimeProvider.System,
            accumulator,
            NullLogger.Instance,
            Array.Empty<IProcessorEnvelopeHook>(),
            CancellationToken.None).ConfigureAwait(false);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(InboxStatus.Completed);
        accumulator.ToResult(1, TimeSpan.Zero).SucceededCount.Should().Be(1);
    }

    [Fact]
    public async Task PipelinedProcessor_when_lease_renewal_fails_should_cancel_dispatch()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = new LeaseFailingInboxStore(new InMemoryInboxStore(timeProvider: clock));
        var dispatchCount = 0;
        var dispatcher = new CountingInboxDispatcher(() => Interlocked.Increment(ref dispatchCount));

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            dispatcher,
            new InboxProcessorOptions
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

        var commandId = Guid.NewGuid();

        await store.Inner.AddAsync(new InboxEnvelope
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

        result.SucceededCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        dispatchCount.Should().BeLessThan(2);
        store.Inner.Get(commandId).Status.Should().Be(InboxStatus.Failed);
        store.Inner.Get(commandId).LastError.Should().Be(MessageProcessorDiagnostics.LeaseLostDuringProcessingError);
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
                if (instrument.Meter.Name == LiteBusInboxTelemetry.MeterName &&
                    instrument.Name == LiteBusInboxTelemetry.ProcessorLeaseLostInstrumentName)
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
        var store = new LeaseFailingInboxStore(new InMemoryInboxStore(timeProvider: clock));

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() =>
            {
            }),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "lease-lost-worker",
                LeaseDuration = TimeSpan.FromSeconds(10),
                LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(50),
                DispatcherConcurrency = 1
            },
            clock,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        measurementCount.Should().BeGreaterThan(0);
        meterListener.Dispose();
    }

    [Fact]
    public async Task PipelinedProcessor_when_after_dispatch_hook_fails_should_not_redispatch_handler()
    {
        var store = new InMemoryInboxStore();
        var dispatchCount = 0;
        var dispatcher = new CountingInboxDispatcher(() => Interlocked.Increment(ref dispatchCount));
        var hook = new ThrowingAfterDispatchHook();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "hook-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            [hook]);

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

        dispatchCount.Should().Be(1);
        result.DeadLetteredCount.Should().Be(1);
        store.Get(commandId).Status.Should().Be(InboxStatus.DeadLettered);
    }

    [Fact]
    public async Task PipelinedProcessor_when_after_dispatch_hook_fails_should_persist_dead_letter_without_completed()
    {
        var store = new PersistRecordingInboxStore(new InMemoryInboxStore());

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() =>
            {
            }),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "hook-persist-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            [new ThrowingAfterDispatchHook()]);

        var commandId = Guid.NewGuid();

        await store.Inner.AddAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        store.PersistedStatuses.Should().Equal(InboxStatus.DeadLettered);
        store.Inner.Get(commandId).Status.Should().Be(InboxStatus.DeadLettered);
    }

    [Fact]
    public async Task PipelinedProcessor_when_hook_failure_policy_is_complete_despite_hook_failure_should_mark_completed()
    {
        var store = new InMemoryInboxStore();
        var dispatchCount = 0;

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() => Interlocked.Increment(ref dispatchCount)),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "hook-complete-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false },
                HookFailurePolicy = ProcessorHookFailurePolicy.CompleteDespiteHookFailure
            },
            TimeProvider.System,
            [new ThrowingAfterDispatchHook()]);

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

        dispatchCount.Should().Be(1);
        result.SucceededCount.Should().Be(1);
        store.Get(commandId).Status.Should().Be(InboxStatus.Completed);
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
                if (instrument.Meter.Name == LiteBusInboxTelemetry.MeterName &&
                    instrument.Name == LiteBusInboxTelemetry.ProcessorPersistSkippedInstrumentName)
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

        var store = new SkippingPersistInboxStore(new InMemoryInboxStore());

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() =>
            {
            }),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-skip-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        measurementCount.Should().Be(1);
        meterListener.Dispose();
    }

    [Fact]
    public async Task PipelinedProcessor_default_options_should_pass_none_to_terminal_persist()
    {
        var store = new TokenCapturingInboxStore(new InMemoryInboxStore());

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() =>
            {
            }),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-token-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                HonorShutdownTokenOnPersist = false
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        await processor.ProcessPendingAsync(cts.Token).ConfigureAwait(false);

        store.LastPersistToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task PipelinedProcessor_when_honor_shutdown_enabled_should_pass_dispatch_token_to_persist()
    {
        var store = new TokenCapturingInboxStore(new InMemoryInboxStore());

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() =>
            {
            }),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-token-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                HonorShutdownTokenOnPersist = true
            },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        await processor.ProcessPendingAsync(cts.Token).ConfigureAwait(false);

        store.LastPersistToken.Should().Be(cts.Token);
    }

    private sealed class CountingInboxDispatcher : IInboxDispatcher
    {
        private readonly Action _onDispatch;

        public CountingInboxDispatcher(Action onDispatch)
        {
            _onDispatch = onDispatch;
        }

        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _onDispatch();
            return Task.Delay(200, cancellationToken);
        }
    }

    private sealed class ThrowingAfterDispatchHook : IProcessorEnvelopeHook
    {
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("AfterDispatch failed.");
        }
    }

    private sealed class LeaseFailingInboxStore : IInboxProcessingStore
    {
        private int _renewalAttempts;

        public LeaseFailingInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryInboxStore Inner { get; }

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            _renewalAttempts++;
            return Task.FromResult(_renewalAttempts == 1);
        }

        public Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            return Inner.PersistAsync(envelopes, cancellationToken);
        }
    }

    private sealed class SkippingPersistInboxStore : IInboxProcessingStore
    {
        public SkippingPersistInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryInboxStore Inner { get; }

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.RenewLeaseAsync(request, cancellationToken);
        }

        public Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PersistResult.FromOutcome(0, envelopes.Count));
        }
    }

    private sealed class PersistRecordingInboxStore : IInboxProcessingStore
    {
        public PersistRecordingInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryInboxStore Inner { get; }

        public List<InboxStatus> PersistedStatuses { get; } = [];

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.RenewLeaseAsync(request, cancellationToken);
        }

        public async Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            foreach (var envelope in envelopes)
            {
                PersistedStatuses.Add(envelope.Status);
            }

            return await Inner.PersistAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TokenCapturingInboxStore : IInboxProcessingStore
    {
        public TokenCapturingInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        public InMemoryInboxStore Inner { get; }

        public CancellationToken LastPersistToken { get; private set; }

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.RenewLeaseAsync(request, cancellationToken);
        }

        public async Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            LastPersistToken = cancellationToken;
            return await Inner.PersistAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }
    }
}