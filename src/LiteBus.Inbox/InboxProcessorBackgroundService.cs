using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Runs the inbox processor in a continuous loop as LiteBus background service work.
/// </summary>
public sealed class InboxProcessorBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Gets the loop timing and adaptive polling options for the processor.
    /// </summary>
    private readonly InboxProcessorHostOptions _hostOptions;

    /// <summary>
    ///     Gets the inbox processor that performs each pass.
    /// </summary>
    private readonly IInboxProcessor _processor;

    /// <summary>
    ///     Gets the batch and lease options used to interpret adaptive polling.
    /// </summary>
    private readonly InboxProcessorOptions _processorOptions;

    /// <summary>
    ///     Gets the signal used to wait for PostgreSQL notifications or polling delays.
    /// </summary>
    private readonly IInboxWorkSignal _workSignal;

    /// <summary>
    ///     Gets the logger used for processor loop diagnostics.
    /// </summary>
    private readonly ILogger<InboxProcessor> _logger;

    /// <summary>
    ///     Gets the control surface used to pause, resume, and drain the processor loop.
    /// </summary>
    private readonly InboxProcessorControl _control;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessorBackgroundService" /> class.
    /// </summary>
    /// <param name="processor">The inbox processor that performs each pass.</param>
    /// <param name="processorOptions">The batch and lease options used to interpret adaptive polling.</param>
    /// <param name="hostOptions">The loop timing and adaptive polling options.</param>
    /// <param name="workSignal">The signal used to wait for work notifications or polling delays.</param>
    /// <param name="control">The control surface used to pause, resume, and drain the processor loop.</param>
    /// <param name="logger">The optional logger for processor loop diagnostics.</param>
    public InboxProcessorBackgroundService(
        IInboxProcessor processor,
        InboxProcessorOptions processorOptions,
        InboxProcessorHostOptions hostOptions,
        IInboxWorkSignal workSignal,
        InboxProcessorControl control,
        ILogger<InboxProcessor>? logger = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _processorOptions = processorOptions ?? throw new ArgumentNullException(nameof(processorOptions));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _workSignal = workSignal ?? throw new ArgumentNullException(nameof(workSignal));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _logger = logger ?? NullLogger<InboxProcessor>.Instance;
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
                InboxProcessorTelemetry.RecordLoopError();
                _logger.LogError(exception, "Inbox processor loop failed; waiting before the next pass.");
                await _workSignal.WaitForWorkOrDelayAsync(_hostOptions.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Determines whether the loop should wait for <see cref="InboxProcessorHostOptions.PollInterval" />
    ///     before the next processing pass.
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
