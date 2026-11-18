using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Logs a warning at startup when in-process outbox dispatch allows envelopes without a handler.
/// </summary>
/// <remarks>
///     Allowing envelopes without a handler marks every message published without effect when the handler assembly
///     was not scanned, while the backlog reads zero. The warning names the acknowledgement recorded through
///     <see cref="InProcessOutboxDispatchOptionsBuilder.AllowEnvelopesWithoutHandler" /> so the choice stays
///     visible in production logs. When <see cref="InProcessOutboxDispatchOptions.RequireHandler" /> is
///     <see langword="true" /> the task completes without logging.
/// </remarks>
public sealed class InProcessOutboxMissingHandlerWarningStartupTask : IStartupTask
{
    /// <summary>
    ///     Gets the dispatch options that record whether envelopes without a handler are allowed.
    /// </summary>
    private readonly InProcessOutboxDispatchOptions _options;

    /// <summary>
    ///     Gets the logger used for the startup warning.
    /// </summary>
    private readonly ILogger<InProcessOutboxMissingHandlerWarningStartupTask> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InProcessOutboxMissingHandlerWarningStartupTask" /> class.
    /// </summary>
    /// <param name="options">The dispatch options that record whether envelopes without a handler are allowed.</param>
    /// <param name="logger">The optional logger for the startup warning.</param>
    public InProcessOutboxMissingHandlerWarningStartupTask(
        InProcessOutboxDispatchOptions options,
        ILogger<InProcessOutboxMissingHandlerWarningStartupTask>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _logger = logger ?? NullLogger<InProcessOutboxMissingHandlerWarningStartupTask>.Instance;
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (_options.RequireHandler)
        {
            return Task.CompletedTask;
        }

        InProcessOutboxDispatchLogMessages.EnvelopesWithoutHandlerAllowed(
            _logger,
            _options.MissingHandlerReason ?? "(no reason recorded)");

        return Task.CompletedTask;
    }
}
