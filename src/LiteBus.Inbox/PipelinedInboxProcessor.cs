using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Pipelined processor that leases a batch, dispatches across parallel workers, and persists each terminal outcome
///     immediately.
/// </summary>
/// <remarks>
///     <para>
///         Each pass writes leased envelopes to a bounded <see cref="Channel{T}" /> consumed by
///         <see cref="InboxProcessorOptions.DispatcherConcurrency" /> workers. Lease heartbeat renewal runs while
///         dispatch is in progress so slow handlers retain ownership until they finish.
///     </para>
///     <para>
///         This is the default implementation selected by <see cref="InboxProcessorOptions.Architecture" />.
///     </para>
/// </remarks>
public sealed class PipelinedInboxProcessor : Abstractions.IInboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to execute each leased envelope.
    /// </summary>
    private readonly IInboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the lease owner name assigned to envelopes claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly InboxProcessorOptions _options;

    /// <summary>
    ///     Gets the processing store used to lease, renew, and persist envelope state transitions.
    /// </summary>
    private readonly IInboxProcessingStore _processingStore;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<PipelinedInboxProcessor> _logger;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IInboxProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelinedInboxProcessor" /> class.
    /// </summary>
    /// <param name="processingStore">The processing store used to lease, renew, and persist envelope state transitions.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public PipelinedInboxProcessor(
        IInboxProcessingStore processingStore,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        IReadOnlyList<IInboxProcessorEnvelopeHook> hooks,
        ILogger<PipelinedInboxProcessor>? logger = null)
    {
        _processingStore = processingStore ?? throw new ArgumentNullException(nameof(processingStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<PipelinedInboxProcessor>.Instance;
        _hooks = hooks ?? Array.Empty<IInboxProcessorEnvelopeHook>();
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        InboxProcessorFactory.ValidateOptions(_options);
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = InboxProcessorTelemetry.ActivitySource.StartActivity("inbox.processor.pass");

        var stopwatch = ProcessorPassStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leasedEnvelopes = await _processingStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration,
            TenantId = _options.TenantId
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Leased {LeasedCount} inbox envelope(s) as owner {LeaseOwner}.",
            leasedEnvelopes.Count,
            _leaseOwner);

        if (leasedEnvelopes.Count == 0)
        {
            return InboxProcessorPassRecorder.FinalizePass(
                new ConcurrentProcessorPassAccumulator<InboxEnvelope>(),
                0,
                stopwatch.GetElapsedTime(),
                passActivity,
                _logger);
        }

        var channel = Channel.CreateBounded<InboxEnvelope>(new BoundedChannelOptions(_options.BatchSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = _options.DispatcherConcurrency == 1
        });

        var accumulator = new ConcurrentProcessorPassAccumulator<InboxEnvelope>();
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

        return InboxProcessorPassRecorder.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }

    /// <summary>
    ///     Consumes leased envelopes from the channel, dispatches them, and persists terminal outcomes.
    /// </summary>
    /// <param name="reader">The channel reader supplying leased envelopes.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>A task that completes when the reader is completed.</returns>
    private async Task RunWorkerAsync(
        ChannelReader<InboxEnvelope> reader,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
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
                token => InboxProcessorEnvelopeHandler.ProcessAsync(
                    envelope,
                    _dispatcher,
                    _options,
                    _clock,
                    accumulator,
                    _logger,
                    _hooks,
                    token),
                cancellationToken).ConfigureAwait(false);

            if (updated is not null)
            {
                await _processingStore.PersistAsync(new[] { updated }, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
