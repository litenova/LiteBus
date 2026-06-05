using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Sequential processor that leases inbox envelopes and dispatches them one at a time through
///     <see cref="IInboxDispatcher" />.
/// </summary>
/// <remarks>
///     <para>
///         Each processing pass leases a bounded batch and calls <see cref="IInboxDispatcher.DispatchAsync" /> per
///         envelope in a single-threaded foreach loop. Select this implementation through
///         <see cref="InboxProcessorOptions.Architecture" /> when the pipelined model is not required.
///     </para>
///     <para>
///         Failures from <see cref="IInboxDispatcher.DispatchAsync" /> are recorded through
///         <see cref="IInboxStateWriter" />. Each terminal outcome is persisted immediately with
///         <see cref="CancellationToken.None" /> so a reclaimed lease cannot re-dispatch in-flight work.
///     </para>
/// </remarks>
public class LegacySequentialInboxProcessor : Abstractions.IInboxProcessor
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
    ///     Gets the processing store used to lease and persist envelope state transitions.
    /// </summary>
    private readonly IInboxProcessingStore _processingStore;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<LegacySequentialInboxProcessor> _logger;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IInboxProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LegacySequentialInboxProcessor" /> class.
    /// </summary>
    /// <param name="processingStore">The processing store used to lease and persist envelope state transitions.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public LegacySequentialInboxProcessor(
        IInboxProcessingStore processingStore,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        IReadOnlyList<IInboxProcessorEnvelopeHook> hooks,
        ILogger<LegacySequentialInboxProcessor>? logger = null)
    {
        _processingStore = processingStore ?? throw new ArgumentNullException(nameof(processingStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<LegacySequentialInboxProcessor>.Instance;
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

        var accumulator = new ProcessorPassAccumulator<InboxEnvelope>();

        try
        {
            foreach (var envelope in leasedEnvelopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var updated = await InboxProcessorEnvelopeHandler.ProcessAsync(
                    envelope,
                    _dispatcher,
                    _options,
                    _clock,
                    accumulator,
                    _logger,
                    _hooks,
                    cancellationToken).ConfigureAwait(false);

                if (updated is not null)
                {
                    await _processingStore.PersistAsync(new[] { updated }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (accumulator.TotalCount > 0)
            {
                await _processingStore.PersistAsync(accumulator.Updates, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return InboxProcessorPassRecorder.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }
}
