using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Pipelined processor that leases a batch, publishes across parallel workers, and persists each terminal outcome
///     immediately.
/// </summary>
/// <remarks>
///     <para>
///         Each pass writes leased messages to a bounded <see cref="Channel{T}" /> consumed by
///         <see cref="OutboxProcessorOptions.DispatcherConcurrency" /> workers. Lease heartbeat renewal runs while
///         publication is in progress so slow dispatchers retain ownership until they finish.
///     </para>
///     <para>
///         This is the default implementation selected by <see cref="OutboxProcessorOptions.Architecture" />.
///     </para>
/// </remarks>
public sealed class PipelinedOutboxProcessor : IOutboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to publish each leased message.
    /// </summary>
    private readonly IOutboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the lease owner name assigned to messages claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly OutboxProcessorOptions _options;

    /// <summary>
    ///     Gets the processing store used to lease, renew, and persist envelope state transitions.
    /// </summary>
    private readonly IOutboxProcessingStore _processingStore;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<PipelinedOutboxProcessor> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelinedOutboxProcessor" /> class.
    /// </summary>
    /// <param name="processingStore">The processing store used to lease, renew, and persist envelope state transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public PipelinedOutboxProcessor(
        IOutboxProcessingStore processingStore,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        ILogger<PipelinedOutboxProcessor>? logger = null)
    {
        _processingStore = processingStore ?? throw new ArgumentNullException(nameof(processingStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<PipelinedOutboxProcessor>.Instance;
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        OutboxProcessorFactory.ValidateOptions(_options);
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity("outbox.processor.pass");

        var stopwatch = ProcessorPassStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leasedEnvelopes = await _processingStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration,
            TenantId = _options.TenantId
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Leased {LeasedCount} outbox message(s) as owner {LeaseOwner}.",
            leasedEnvelopes.Count,
            _leaseOwner);

        if (leasedEnvelopes.Count == 0)
        {
            return OutboxProcessorPassRecorder.FinalizePass(
                new ConcurrentProcessorPassAccumulator<OutboxEnvelope>(),
                0,
                stopwatch.GetElapsedTime(),
                passActivity,
                _logger);
        }

        var channel = Channel.CreateBounded<OutboxEnvelope>(new BoundedChannelOptions(_options.BatchSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = _options.DispatcherConcurrency == 1
        });

        var accumulator = new ConcurrentProcessorPassAccumulator<OutboxEnvelope>();
        var workers = new Task[_options.DispatcherConcurrency];

        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = RunWorkerAsync(channel.Reader, accumulator, cancellationToken);
        }

        foreach (var envelope in leasedEnvelopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
        }

        channel.Writer.Complete();
        await Task.WhenAll(workers).ConfigureAwait(false);

        return OutboxProcessorPassRecorder.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }

    /// <summary>
    ///     Consumes leased messages from the channel, publishes them, and persists terminal outcomes.
    /// </summary>
    /// <param name="reader">The channel reader supplying leased messages.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>A task that completes when the reader is completed.</returns>
    private async Task RunWorkerAsync(
        ChannelReader<OutboxEnvelope> reader,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        await foreach (var envelope in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var updated = await ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
                envelope.Id,
                _leaseOwner,
                _processingStore,
                _options.LeaseDuration,
                _options.LeaseHeartbeatInterval,
                _clock,
                token => OutboxProcessorEnvelopeHandler.ProcessAsync(
                    envelope,
                    _dispatcher,
                    _options,
                    _clock,
                    accumulator,
                    _logger,
                    token),
                cancellationToken).ConfigureAwait(false);

            if (updated is not null)
            {
                await _processingStore.PersistAsync(new[] { updated }, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
