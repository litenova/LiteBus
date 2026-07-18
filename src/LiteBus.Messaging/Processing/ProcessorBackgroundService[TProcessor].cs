using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Runs a durable message processor in a continuous loop as LiteBus background service work.
/// </summary>
/// <typeparam name="TProcessor">The processor type that performs each pass.</typeparam>
internal sealed class ProcessorBackgroundService<TProcessor> : IBackgroundService
    where TProcessor : class, IMessageProcessor
{
    /// <summary>
    ///     Gets the control surface used to pause, resume, and drain the processor loop.
    /// </summary>
    private readonly IProcessorBackgroundControl _control;

    /// <summary>
    ///     Gets the loop timing and adaptive polling options for the processor.
    /// </summary>
    private readonly ProcessorHostOptions _hostOptions;

    /// <summary>
    ///     Gets the logger used for processor loop diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     Gets the callback invoked to log loop failures.
    /// </summary>
    private readonly Action<ILogger, Exception> _logLoopFailed;

    /// <summary>
    ///     Gets the processor that performs each pass.
    /// </summary>
    private readonly TProcessor _processor;

    /// <summary>
    ///     Gets the batch options used to interpret adaptive polling.
    /// </summary>
    private readonly ProcessorOptions _processorOptions;

    /// <summary>
    ///     Gets the callback invoked when the loop catches an unexpected exception.
    /// </summary>
    private readonly Action _recordLoopError;

    /// <summary>
    ///     Gets the signal used to wait for work notifications or polling delays.
    /// </summary>
    private readonly IProcessorWorkSignal _workSignal;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProcessorBackgroundService{TProcessor}" /> class.
    /// </summary>
    /// <param name="processor">The processor that performs each pass.</param>
    /// <param name="processorOptions">The batch options used to interpret adaptive polling.</param>
    /// <param name="hostOptions">The loop timing and adaptive polling options.</param>
    /// <param name="workSignal">The signal used to wait for work notifications or polling delays.</param>
    /// <param name="control">The control surface used to pause, resume, and drain the processor loop.</param>
    /// <param name="recordLoopError">The callback invoked when the loop catches an unexpected exception.</param>
    /// <param name="logLoopFailed">The callback invoked to log loop failures.</param>
    /// <param name="logger">The logger used for processor loop diagnostics.</param>
    public ProcessorBackgroundService(
        TProcessor processor,
        ProcessorOptions processorOptions,
        ProcessorHostOptions hostOptions,
        IProcessorWorkSignal workSignal,
        IProcessorBackgroundControl control,
        Action recordLoopError,
        Action<ILogger, Exception> logLoopFailed,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(processorOptions);
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(workSignal);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(recordLoopError);
        ArgumentNullException.ThrowIfNull(logLoopFailed);
        ArgumentNullException.ThrowIfNull(logger);
        _processor = processor;
        _processorOptions = processorOptions;
        _hostOptions = hostOptions;
        _workSignal = workSignal;
        _control = control;
        _recordLoopError = recordLoopError;
        _logLoopFailed = logLoopFailed;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hostOptions.Enabled)
        {
            return;
        }

        if (_hostOptions.StartupDelay > TimeSpan.Zero)
        {
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                _control.DrainRequestedToken);

            try
            {
                await Task.Delay(_hostOptions.StartupDelay, startupCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_control.IsDraining && !stoppingToken.IsCancellationRequested)
            {
                // Drain bypasses startup delay so the loop can run its final pass immediately.
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await _control.WaitIfPausedAsync(stoppingToken).ConfigureAwait(false);
            var drainPass = _control.IsDraining;
            ProcessorPassResult? passResult = null;

            try
            {
                passResult = await _processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (stoppingToken.IsCancellationRequested)
            {
                if (drainPass)
                {
                    _control.SignalDrainComplete(exception);
                }

                return;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // Background processor loops must survive transient store or dispatch failures and continue polling.
                _recordLoopError();
                _logLoopFailed(_logger, exception);

                if (drainPass)
                {
                    _control.SignalDrainComplete(exception);
                    throw;
                }
            }
#pragma warning restore CA1031
            finally
            {
                _control.SignalPassComplete();
            }

            if (drainPass)
            {
                _control.SignalDrainComplete(null);
                return;
            }

            if (passResult is null || ShouldDelayAfterPass(passResult))
            {
                try
                {
                    await WaitForWorkOrDrainAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
#pragma warning disable CA1031 // The polling boundary must keep the processor alive when a work signal fails.
                catch (Exception exception)
                {
                    _recordLoopError();
                    _logLoopFailed(_logger, exception);
                }
#pragma warning restore CA1031
            }
        }
    }

    /// <summary>
    ///     Waits for work while allowing a drain request to interrupt the polling delay immediately.
    /// </summary>
    /// <param name="stoppingToken">A token that stops the background loop.</param>
    /// <returns>A task that completes when work, drain, or host cancellation ends the wait.</returns>
    private async Task WaitForWorkOrDrainAsync(CancellationToken stoppingToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _control.DrainRequestedToken);

        try
        {
            await _workSignal
                .WaitForWorkOrDelayAsync(_hostOptions.PollInterval, waitCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_control.IsDraining && !stoppingToken.IsCancellationRequested)
        {
            // Drain cancellation only wakes the loop so it can enter its final pass without waiting for the poll interval.
        }
    }

    /// <summary>
    ///     Determines whether the loop should wait for the poll interval before the next processing pass.
    /// </summary>
    /// <param name="passResult">The result from the pass that just completed.</param>
    /// <returns>
    ///     <see langword="true" /> when the loop should delay before leasing again; otherwise <see langword="false" />.
    /// </returns>
    private bool ShouldDelayAfterPass(ProcessorPassResult passResult)
    {
        if (_hostOptions.PollInterval <= TimeSpan.Zero)
        {
            return false;
        }

        if (_hostOptions.UseAdaptivePolling && passResult.LeasedCount >= _processorOptions.BatchSize)
        {
            return false;
        }

        return true;
    }
}
