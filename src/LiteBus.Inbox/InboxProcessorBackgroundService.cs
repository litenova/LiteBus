using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Processing;
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
    ///     Gets the shared processor background loop configured for inbox work signals and control.
    /// </summary>
    private readonly ProcessorBackgroundService<IInboxProcessor> _loop;

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
        ILogger<InboxProcessorBackgroundService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(processorOptions);
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(workSignal);
        ArgumentNullException.ThrowIfNull(control);

        var resolvedLogger = logger ?? NullLogger<InboxProcessorBackgroundService>.Instance;

        _loop = new ProcessorBackgroundService<IInboxProcessor>(
            processor,
            processorOptions,
            hostOptions,
            workSignal,
            control,
            InboxProcessorTelemetry.RecordLoopError,
            InboxProcessorLogMessages.LoopFailed,
            resolvedLogger);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _loop.ExecuteAsync(stoppingToken);
    }
}