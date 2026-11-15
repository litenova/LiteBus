using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Runs the recurring loop shared by every recurring background service: options snapshot, startup delay, lease
///     acquisition and loss, conflict and error backoff, busy periods while a pass reports more work, and shutdown.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="RecurringCommandBackgroundService{TCommand}" /> and
///         the recurring batch background service differ only in what one pass does. Each gives
///         the loop a factory that builds its pass from the service provider of the loop's scope; the pass returns
///         <see langword="true" /> when it did as much work as it was allowed to and the next pass should start at once.
///     </para>
///     <para>
///         A pass that throws is logged and the loop continues after
///         <see cref="RecurringCommandHostOptions.ErrorBackoff" />. A pass whose exception
///         <see cref="RecurringCommandHostOptions.IsConcurrencyConflict" /> classifies as a lost race waits
///         <see cref="RecurringCommandHostOptions.Interval" /> instead. An exception never escapes the loop.
///     </para>
/// </remarks>
internal sealed class RecurringCommandLoop
{
    /// <summary>
    ///     The lifecycle and timing settings for this loop.
    /// </summary>
    private readonly RecurringCommandHostOptions _options;

    /// <summary>
    ///     Opens the scope the pass and the lease are resolved from once when the loop starts.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _scopeFactory;

    /// <summary>
    ///     Supplies the delay between passes.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Records pass failures and lifecycle transitions under the owning service's category.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     The name the log messages use for this loop, such as the command type name.
    /// </summary>
    private readonly string _displayName;

    /// <summary>
    ///     The lease name used when <see cref="RecurringCommandHostOptions.LeaseName" /> is not set.
    /// </summary>
    private readonly string _defaultLeaseName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandLoop" /> class.
    /// </summary>
    /// <param name="options">The lifecycle and timing settings for this loop.</param>
    /// <param name="scopeFactory">Opens the scope the pass and the lease are resolved from.</param>
    /// <param name="timeProvider">Supplies the delay between passes.</param>
    /// <param name="logger">Records pass failures and lifecycle transitions.</param>
    /// <param name="displayName">The name the log messages use for this loop.</param>
    /// <param name="defaultLeaseName">The lease name used when the options name none.</param>
    public RecurringCommandLoop(
        RecurringCommandHostOptions options,
        IMessageDispatchScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger logger,
        string displayName,
        string defaultLeaseName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLeaseName);

        _options = options;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _displayName = displayName;
        _defaultLeaseName = defaultLeaseName;
    }

    /// <summary>
    ///     Runs the loop until <paramref name="stoppingToken" /> is cancelled.
    /// </summary>
    /// <param name="createPass">
    ///     Builds the pass from the service provider of the loop's scope. It runs once, after the enabled check, and
    ///     throws <see cref="LiteBusDependencyResolutionException" /> when a service the pass needs is missing.
    /// </param>
    /// <param name="stoppingToken">The token that stops the loop when the host shuts down.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    public async Task RunAsync(
        Func<IServiceProvider, Func<CancellationToken, Task<bool>>> createPass,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(createPass);

        _options.Validate();

        // The options live on a shared singleton with public setters, so the validated values the loop reads on every
        // pass are snapshotted here. A post-start mutation then cannot turn the loop into an unthrottled send loop by
        // zeroing the interval.
        var interval = _options.Interval;
        var errorBackoff = _options.ErrorBackoff;
        var isConcurrencyConflict = _options.IsConcurrencyConflict;

        if (!_options.Enabled)
        {
            RecurringCommandLogMessages.LoopDisabled(_logger, _displayName);
            return;
        }

        var loopScope = _scopeFactory.CreateScope();
        await using var configuredLoopScope = loopScope.ConfigureAwait(false);
        var passAsync = createPass(loopScope.ServiceProvider);

        IRecurringCommandLease? leaseService = null;
        if (_options.RequireLease)
        {
            leaseService = (IRecurringCommandLease?) loopScope.ServiceProvider.GetService(typeof(IRecurringCommandLease));
            if (leaseService is null)
            {
                throw new LiteBusDependencyResolutionException(typeof(IRecurringCommandLease));
            }
        }

        var leaseName = _options.LeaseName ?? _defaultLeaseName;

        if (_options.StartupDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(_options.StartupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        RecurringCommandLogMessages.LoopStarted(_logger, _displayName, interval);

        IRecurringCommandLeaseHandle? lease = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = interval;

                try
                {
                    if (leaseService is not null && lease is null)
                    {
                        lease = await leaseService.TryAcquireAsync(leaseName, stoppingToken).ConfigureAwait(false);

                        if (lease is null)
                        {
                            RecurringCommandLogMessages.LeaseSkipped(_logger, _displayName, leaseName);
                        }
                    }

                    if (lease is not null && lease.LeaseLost.IsCancellationRequested)
                    {
                        // The renewal loop stopped defending the row, so another replica may already be running this
                        // pass. The busy period ends here: the handle is released below and the loop backs off instead
                        // of sending undefended.
                        RecurringCommandLogMessages.LeaseLost(_logger, _displayName, leaseName);
                        delay = errorBackoff;
                    }
                    else if (leaseService is null || lease is not null)
                    {
                        bool hasMoreWork;

                        if (lease is null)
                        {
                            hasMoreWork = await passAsync(stoppingToken).ConfigureAwait(false);
                        }
                        else
                        {
                            using var linkedLoss = CancellationTokenSource.CreateLinkedTokenSource(
                                stoppingToken,
                                lease.LeaseLost);
                            hasMoreWork = await passAsync(linkedLoss.Token).ConfigureAwait(false);
                        }

                        if (lease is not null && lease.LeaseLost.IsCancellationRequested)
                        {
                            // Lost mid-pass: the busy period ends even when the result reports more work.
                            RecurringCommandLogMessages.LeaseLost(_logger, _displayName, leaseName);
                            delay = errorBackoff;
                        }
                        else if (hasMoreWork)
                        {
                            // The pass did as much as it was allowed to, so the next one starts without waiting. This is what
                            // turns a large backlog into one busy period instead of one batch per interval. The lease stays
                            // held across the busy period and is released before the next wait. A send that completes
                            // synchronously would otherwise spin the loop on the caller's thread and never hand
                            // ExecuteAsync back to the host that started it.
                            await Task.Yield();
                            continue;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException) when (lease is not null && lease.LeaseLost.IsCancellationRequested)
                {
                    // The in-flight send observed lease loss through the linked token. The busy period ends: the
                    // handle is released below and the loop backs off.
                    RecurringCommandLogMessages.LeaseLost(_logger, _displayName, leaseName);
                    delay = errorBackoff;
                }
                catch (RecurringPassBackoffException)
                {
                    // The pass already logged why it ended; it asks for the error backoff without being a fault.
                    delay = errorBackoff;
                }
                catch (Exception exception) when (isConcurrencyConflict?.Invoke(exception) == true)
                {
                    // Another instance won the race for the same data, so the work this send wanted is already done or
                    // will be retried by the next pass. Waiting out the error backoff would only delay that pass.
                    RecurringCommandLogMessages.PassConflicted(_logger, _displayName, exception.GetType().Name);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception exception)
                {
                    // A recurring loop must outlive the failures of what it calls; ending here would silently stop the
                    // work for the life of the process, which is the failure this catch exists to prevent.
                    RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);
                    delay = errorBackoff;
                }
#pragma warning restore CA1031

#pragma warning disable CA1031 // Do not catch general exception types
                try
                {
                    if (lease is not null)
                    {
                        await lease.DisposeAsync().ConfigureAwait(false);
                        lease = null;
                    }
                }
                catch (Exception exception)
                {
                    // Releasing the lease is part of the pass: a store that dropped the holding connection reports the
                    // failure here and backs off, rather than ending the loop.
                    lease = null;
                    RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);
                    delay = errorBackoff;
                }
#pragma warning restore CA1031

                if (delay <= TimeSpan.Zero)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            if (lease is not null)
            {
#pragma warning disable CA1031 // Do not catch general exception types
                try
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Shutdown must not fault because the store is unreachable: the loop is over either way, and the
                    // row expires on its own when the release delete cannot run.
                    RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);
                }
#pragma warning restore CA1031
            }
        }
    }
}
