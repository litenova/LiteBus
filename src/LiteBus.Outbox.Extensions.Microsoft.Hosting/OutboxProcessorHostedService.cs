using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Runs the outbox processor in a continuous loop as a generic-host background service.
/// </summary>
public sealed class OutboxProcessorHostedService : BackgroundService
{
    /// <summary>
    ///     Gets the time provider used to record pass timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the loop timing and adaptive polling options for the hosted processor.
    /// </summary>
    private readonly OutboxProcessorHostOptions _hostOptions;

    /// <summary>
    ///     Gets the outbox processor that performs each pass.
    /// </summary>
    private readonly IOutboxProcessor _processor;

    /// <summary>
    ///     Gets the batch and lease options used to interpret adaptive polling.
    /// </summary>
    private readonly OutboxProcessorOptions _processorOptions;

    /// <summary>
    ///     Gets the application service provider used for startup validation.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Gets the mutable host state published to health checks.
    /// </summary>
    private readonly OutboxProcessorHostState _state;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorHostedService" /> class.
    /// </summary>
    /// <param name="processor">The outbox processor that performs each pass.</param>
    /// <param name="processorOptions">The batch and lease options used to interpret adaptive polling.</param>
    /// <param name="hostOptions">The loop timing and adaptive polling options.</param>
    /// <param name="state">The mutable host state published to health checks.</param>
    /// <param name="clock">The time provider used to record pass timestamps.</param>
    /// <param name="serviceProvider">The application service provider used for startup validation.</param>
    public OutboxProcessorHostedService(
        IOutboxProcessor processor,
        OutboxProcessorOptions processorOptions,
        OutboxProcessorHostOptions hostOptions,
        OutboxProcessorHostState state,
        TimeProvider clock,
        IServiceProvider serviceProvider)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _processorOptions = processorOptions ?? throw new ArgumentNullException(nameof(processorOptions));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxHostingDependencyValidator.Validate(_serviceProvider);

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
                _state.RecordSuccessfulPass(_clock.GetUtcNow());

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
                _state.RecordFailure(_clock.GetUtcNow(), MessageProcessorDiagnostics.FormatError(exception));
                await Task.Delay(_hostOptions.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Determines whether the host should wait for <see cref="OutboxProcessorHostOptions.PollInterval" />
    ///     before the next processing pass.
    /// </summary>
    /// <param name="passResult">The result from the pass that just completed.</param>
    /// <returns>
    ///     <see langword="true" /> when the host should delay before leasing again; otherwise <see langword="false" />.
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
