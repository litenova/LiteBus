using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Runs the outbox processor in a continuous loop as LiteBus background service work.
/// </summary>
public sealed class OutboxProcessorBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Gets the outbox processor that performs each pass.
    /// </summary>
    private readonly IOutboxProcessor _processor;

    /// <summary>
    ///     Gets the batch and lease options used to interpret adaptive polling.
    /// </summary>
    private readonly OutboxProcessorOptions _processorOptions;

    /// <summary>
    ///     Gets the loop timing and adaptive polling options for the processor.
    /// </summary>
    private readonly OutboxProcessorHostOptions _hostOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorBackgroundService" /> class.
    /// </summary>
    /// <param name="processor">The outbox processor that performs each pass.</param>
    /// <param name="processorOptions">The batch and lease options used to interpret adaptive polling.</param>
    /// <param name="hostOptions">The loop timing and adaptive polling options.</param>
    public OutboxProcessorBackgroundService(
        IOutboxProcessor processor,
        OutboxProcessorOptions processorOptions,
        OutboxProcessorHostOptions hostOptions)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _processorOptions = processorOptions ?? throw new ArgumentNullException(nameof(processorOptions));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
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
            try
            {
                var passResult = await _processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);

                if (ShouldDelayAfterPass(passResult))
                {
                    await Task.Delay(_hostOptions.PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _ = MessageProcessorDiagnostics.FormatError(exception);
                await Task.Delay(_hostOptions.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Determines whether the loop should wait for <see cref="OutboxProcessorHostOptions.PollInterval" />
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
