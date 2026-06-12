using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Pipelined processor that leases a batch, dispatches across parallel workers, and persists each terminal outcome
///     immediately.
/// </summary>
/// <typeparam name="TEnvelope">The leased envelope type processed by the worker.</typeparam>
/// <typeparam name="TOptions">The processor options type.</typeparam>
internal sealed class PipelinedMessageProcessor<TEnvelope, TOptions>
    where TOptions : ProcessorOptions
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the lease owner name assigned to messages claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the lease store used to claim and renew message ownership during processing.
    /// </summary>
    private readonly ILeaseRenewable _leaseStore;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     Gets the axis-specific operations used for leasing, dispatch, and persistence.
    /// </summary>
    private readonly IPipelinedMessageProcessorOperations<TEnvelope, TOptions> _operations;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly TOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelinedMessageProcessor{TEnvelope, TOptions}" /> class.
    /// </summary>
    /// <param name="leaseStore">The lease store used to claim and renew message ownership during processing.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="operations">The axis-specific operations used for leasing, dispatch, and persistence.</param>
    /// <param name="logger">The logger used for lease, pass, and dispatch diagnostics.</param>
    public PipelinedMessageProcessor(
        ILeaseRenewable leaseStore,
        TOptions options,
        TimeProvider clock,
        IPipelinedMessageProcessorOperations<TEnvelope, TOptions> operations,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(logger);
        _leaseStore = leaseStore;
        _options = options;
        _clock = clock;
        _operations = operations;
        _logger = logger;

        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;
    }

    /// <summary>
    ///     Processes one batch of due messages.
    /// </summary>
    /// <param name="cancellationToken">A token used to stop leasing or dispatch.</param>
    /// <returns>A pass result that reports how many messages were leased during the pass.</returns>
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = _operations.StartPassActivity();

        var stopwatch = ProcessorPassStopwatch.StartNew();
        var now = _clock.GetUtcNow();

        var leasedEnvelopes = await _operations.LeasePendingAsync(_leaseOwner, _options, now, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            _operations.LeasedBatchDebugMessage,
            leasedEnvelopes.Count,
            _leaseOwner);

        _operations.RecordLeasesAcquired(leasedEnvelopes.Count);

        if (leasedEnvelopes.Count == 0)
        {
            return _operations.FinalizePass(
                new ConcurrentProcessorPassAccumulator<TEnvelope>(),
                0,
                stopwatch.GetElapsedTime(),
                passActivity,
                _logger);
        }

        var channel = Channel.CreateBounded<TEnvelope>(new BoundedChannelOptions(_options.BatchSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = _options.DispatcherConcurrency == 1
        });

        var accumulator = new ConcurrentProcessorPassAccumulator<TEnvelope>();
        var workers = new Task[_options.DispatcherConcurrency];

        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = RunWorkerAsync(channel.Reader, accumulator, cancellationToken);
        }

        try
        {
            foreach (var envelope in leasedEnvelopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
            }

            channel.Writer.Complete();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            channel.Writer.TryComplete();

            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        return _operations.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }

    /// <summary>
    ///     Consumes leased messages from the channel, dispatches them, and persists terminal outcomes.
    /// </summary>
    /// <param name="reader">The channel reader supplying leased messages.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>A task that completes when the reader is completed.</returns>
    private async Task RunWorkerAsync(
        ChannelReader<TEnvelope> reader,
        ConcurrentProcessorPassAccumulator<TEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        await foreach (var envelope in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            TEnvelope? updated;

            try
            {
                updated = await ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
                    new LeaseHeartbeatContext(
                        _operations.GetMessageId(envelope),
                        _leaseOwner,
                        _leaseStore,
                        _options.LeaseDuration,
                        _options.LeaseHeartbeatInterval,
                        _clock,
                        _operations.LeaseRenewalFailedMessage,
                        _operations.RecordLeaseLost,
                        _logger),
                    token => _operations.DispatchEnvelopeAsync(envelope, _options, token),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                await _operations.PersistLeaseLossOutcomeAsync(
                    envelope,
                    accumulator,
                    _options,
                    _logger,
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            if (updated is null)
            {
                continue;
            }

            try
            {
                await _operations.PersistTerminalOutcomeAsync(
                    envelope,
                    updated,
                    accumulator,
                    _options,
                    _logger,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // One envelope persistence failure must not abort the entire processor pass.
                _logger.LogError(
                    exception,
                    "Terminal persistence failed for message {MessageId}. Continuing the pass with remaining envelopes.",
                    _operations.GetMessageId(envelope));
            }
#pragma warning restore CA1031
        }
    }
}