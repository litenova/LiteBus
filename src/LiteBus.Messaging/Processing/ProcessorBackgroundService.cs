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
    ///     Gets the processor that performs each pass.
    /// </summary>
    private readonly TProcessor _processor;

    /// <summary>
    ///     Gets the batch options used to interpret adaptive polling.
    /// </summary>
    private readonly ProcessorOptions _processorOptions;

    /// <summary>
    ///     Gets the loop timing and adaptive polling options for the processor.
    /// </summary>
    private readonly ProcessorHostOptions _hostOptions;

    /// <summary>
    ///     Gets the signal used to wait for work notifications or polling delays.
    /// </summary>
    private readonly IProcessorWorkSignal _workSignal;

    /// <summary>
    ///     Gets the control surface used to pause, resume, and drain the processor loop.
    /// </summary>
    private readonly IProcessorBackgroundControl _control;

    /// <summary>
    ///     Gets the callback invoked when the loop catches an unexpected exception.
    /// </summary>
    private readonly Action _recordLoopError;

    /// <summary>
    ///     Gets the callback invoked to log loop failures.
    /// </summary>
    private readonly Action<ILogger, Exception> _logLoopFailed;

    /// <summary>
    ///     Gets the logger used for processor loop diagnostics.
    /// </summary>
    private readonly ILogger _logger;

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
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _processorOptions = processorOptions ?? throw new ArgumentNullException(nameof(processorOptions));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _workSignal = workSignal ?? throw new ArgumentNullException(nameof(workSignal));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _recordLoopError = recordLoopError ?? throw new ArgumentNullException(nameof(recordLoopError));
        _logLoopFailed = logLoopFailed ?? throw new ArgumentNullException(nameof(logLoopFailed));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            await Task.Delay(_hostOptions.StartupDelay, stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await _control.WaitIfPausedAsync(stoppingToken).ConfigureAwait(false);

            if (_control.IsDraining)
            {
                try
                {
                    await _processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }

                _control.SignalDrainComplete();
                return;
            }

            try
            {
                var passResult = await _processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);

                if (ShouldDelayAfterPass(passResult))
                {
                    await _workSignal.WaitForWorkOrDelayAsync(_hostOptions.PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _recordLoopError();
                _logLoopFailed(_logger, exception);
                await _workSignal.WaitForWorkOrDelayAsync(_hostOptions.PollInterval, stoppingToken).ConfigureAwait(false);
            }
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
