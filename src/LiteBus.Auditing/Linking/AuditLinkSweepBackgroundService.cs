using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     Links the staged audit records a crash left unlinked, on the sweep interval.
/// </summary>
/// <remarks>
///     <para>
///         The <see cref="LiteBus.Auditing.Abstractions.AuditLink.AfterCommit" /> path links inline
///         right after commit; this loop covers what a crash interrupted by sweeping every
///         <see cref="AuditLinkSweepHostOptions.Interval" /> for records staged longer ago than
///         <see cref="AuditLinkSweepHostOptions.OlderThan" />. A sweep that fails is logged with its
///         exception and retried on the next interval, so a store outage never loses staged records
///         silently.
///     </para>
/// </remarks>
internal sealed class AuditLinkSweepBackgroundService : IBackgroundService
{
    /// <summary>
    ///     The linker that assigns chain positions to staged records. Held for the life of the service.
    /// </summary>
    private readonly IAuditLinker _linker;

    /// <summary>
    ///     The loop timing and sweep age options. Held for the life of the service.
    /// </summary>
    private readonly AuditLinkSweepHostOptions _hostOptions;

    /// <summary>
    ///     The clock that measures sweep intervals. Held for the life of the service.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The logger that records sweep failures. Held for the life of the service.
    /// </summary>
    private readonly ILogger<AuditLinkSweepBackgroundService> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditLinkSweepBackgroundService" /> class.
    /// </summary>
    /// <param name="linker">The linker that assigns chain positions to staged records.</param>
    /// <param name="hostOptions">The loop timing and sweep age options.</param>
    /// <param name="timeProvider">
    ///     The clock that measures sweep intervals, or <see langword="null" /> for
    ///     <see cref="TimeProvider.System" />.
    /// </param>
    /// <param name="logger">The optional logger for sweep diagnostics.</param>
    public AuditLinkSweepBackgroundService(
        IAuditLinker linker,
        AuditLinkSweepHostOptions hostOptions,
        TimeProvider? timeProvider = null,
        ILogger<AuditLinkSweepBackgroundService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(linker);
        ArgumentNullException.ThrowIfNull(hostOptions);

        _linker = linker;
        _hostOptions = hostOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AuditLinkSweepBackgroundService>.Instance;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hostOptions.Validate();

        if (!_hostOptions.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _linker.SweepAsync(_hostOptions.OlderThan, stoppingToken).ConfigureAwait(false);
                await Task.Delay(_hostOptions.Interval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The sweep must survive any store failure and retry on the next interval.
            catch (Exception exception)
            {
                AuditLinkSweepLogMessages.SweepFailed(_logger, exception, _hostOptions.Interval);

                try
                {
                    await Task.Delay(_hostOptions.Interval, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
#pragma warning restore CA1031
        }
    }
}
