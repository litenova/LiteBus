using System.Diagnostics.Metrics;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox processor telemetry when terminal persistence throws.
/// </summary>
public sealed class InboxProcessorPersistFailedTelemetryTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies that a swallowed terminal persist exception increments the persist_failed counter.
    /// </summary>
    /// <returns>A task that completes when the metric assertion succeeds.</returns>
    [Fact]
    public async Task PipelinedProcessor_when_persist_throws_should_increment_persist_failed_metric()
    {
        long measurementCount = 0;
        MeterListener? meterListener = null;

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == LiteBusInboxTelemetry.MeterName &&
                    instrument.Name == LiteBusInboxTelemetry.ProcessorPersistFailedInstrumentName)
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

        var store = new ThrowingPersistInboxStore(new InMemoryInboxStore());

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new NoOpInboxDispatcher(),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "persist-failed-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1
            },
            TimeProvider.System,
            []);

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

    /// <summary>
    ///     No-op dispatcher used when persist failure is the behavior under test.
    /// </summary>
    private sealed class NoOpInboxDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Throws on terminal persist to simulate a store failure after successful dispatch.
    /// </summary>
    private sealed class ThrowingPersistInboxStore : IInboxProcessingStore
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ThrowingPersistInboxStore" /> class.
        /// </summary>
        /// <param name="inner">The underlying in-memory store.</param>
        public ThrowingPersistInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        /// <summary>
        ///     Gets the underlying in-memory store.
        /// </summary>
        public InMemoryInboxStore Inner { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.RenewLeaseAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated terminal persist failure.");
        }
    }
}
