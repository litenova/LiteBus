using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Runs the outbox processor in a continuous loop as LiteBus background service work.
/// </summary>
public sealed class OutboxProcessorBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Gets the shared processor background loop configured for outbox work signals and control.
    /// </summary>
    private readonly ProcessorBackgroundService<IOutboxProcessor> _loop;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorBackgroundService" /> class.
    /// </summary>
    /// <param name="processor">The outbox processor that performs each pass.</param>
    /// <param name="processorOptions">The batch and lease options used to interpret adaptive polling.</param>
    /// <param name="hostOptions">The loop timing and adaptive polling options.</param>
    /// <param name="workSignal">The signal used to wait for work notifications or polling delays.</param>
    /// <param name="control">The control surface used to pause, resume, and drain the processor loop.</param>
    /// <param name="logger">The optional logger for processor loop diagnostics.</param>
    public OutboxProcessorBackgroundService(
        IOutboxProcessor processor,
        OutboxProcessorOptions processorOptions,
        OutboxProcessorHostOptions hostOptions,
        IOutboxWorkSignal workSignal,
        OutboxProcessorControl control,
        ILogger<OutboxProcessorBackgroundService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(processorOptions);
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(workSignal);
        ArgumentNullException.ThrowIfNull(control);

        var resolvedLogger = logger ?? NullLogger<OutboxProcessorBackgroundService>.Instance;

        _loop = new ProcessorBackgroundService<IOutboxProcessor>(
            processor,
            processorOptions,
            hostOptions,
            workSignal,
            control,
            OutboxProcessorTelemetry.RecordLoopError,
            OutboxProcessorLogMessages.LoopFailed,
            resolvedLogger);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _loop.ExecuteAsync(stoppingToken);
    }
}