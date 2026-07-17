using System;
using System.Linq;
using System.Threading;
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

        var configuredLeaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}"
            : _options.LeaseOwner;

        // The opaque session suffix fences processor instances that share a configured owner. A reclaimed row
        // therefore cannot be renewed or completed by the stale processor session.
        _leaseOwner = $"{configuredLeaseOwner}:{Guid.NewGuid():N}";
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

        MessageProcessorLogMessages.LeasedBatch(
            _logger,
            _operations.ProcessorName,
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

        var accumulator = new ConcurrentProcessorPassAccumulator<TEnvelope>();
        using var workerSlots = new SemaphoreSlim(_options.DispatcherConcurrency, _options.DispatcherConcurrency);
        var workers = leasedEnvelopes
            .Select(envelope => RunEnvelopeAsync(envelope, accumulator, workerSlots, cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _operations.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }

    /// <summary>
    ///     Keeps one leased envelope alive from acquisition through dispatch and terminal persistence.
    /// </summary>
    /// <param name="envelope">The leased envelope.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="workerSlots">The semaphore limiting active dispatch workers.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>A task that completes when the envelope reaches a terminal outcome or is abandoned.</returns>
    private async Task RunEnvelopeAsync(
        TEnvelope envelope,
        ConcurrentProcessorPassAccumulator<TEnvelope> accumulator,
        SemaphoreSlim workerSlots,
        CancellationToken cancellationToken)
    {
        var heartbeatContext = CreateHeartbeatContext(envelope);
        var dispatchCompleted = false;

        try
        {
            await ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
                    heartbeatContext,
                    async (heartbeatToken, shutdownToken) =>
                    {
                        await workerSlots.WaitAsync(heartbeatToken).ConfigureAwait(false);

                        try
                        {
                            var updated = await _operations.DispatchEnvelopeAsync(envelope, _options, heartbeatToken)
                                .ConfigureAwait(false);
                            dispatchCompleted = true;

                            if (updated is null)
                            {
                                return true;
                            }

                            await _operations.PersistTerminalOutcomeAsync(
                                    envelope,
                                    updated,
                                    accumulator,
                                    _options,
                                    _logger,
                                    _options.HonorShutdownTokenOnPersist ? shutdownToken : heartbeatToken)
                                .ConfigureAwait(false);

                            return true;
                        }
                        finally
                        {
                            workerSlots.Release();
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
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
                    cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // One envelope persistence failure must not abort the entire processor pass.
        catch (Exception) when (!dispatchCompleted)
        {
            throw;
        }
        catch (Exception exception)
        {
            MessageProcessorLogMessages.TerminalPersistenceFailed(
                _logger,
                _operations.GetMessageId(envelope),
                exception);

            _operations.RecordPersistFailed();
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Creates the lease heartbeat inputs for one envelope and reuses the same owner for terminal persistence.
    /// </summary>
    /// <param name="envelope">The envelope whose lease is being maintained.</param>
    /// <returns>The heartbeat context for dispatch and terminal persistence.</returns>
    private LeaseHeartbeatContext CreateHeartbeatContext(TEnvelope envelope)
    {
        return new LeaseHeartbeatContext(
            _operations.GetMessageId(envelope),
            _leaseOwner,
            _leaseStore,
            _options.LeaseDuration,
            _options.LeaseHeartbeatInterval,
            _clock,
            _operations.ProcessorName,
            _operations.RecordLeaseLost,
            _logger);
    }
}
