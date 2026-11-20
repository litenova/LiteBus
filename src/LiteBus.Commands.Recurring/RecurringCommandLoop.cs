using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Scheduling;
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
///     <para>
///         When <see cref="RecurringCommandHostOptions.Schedule" /> is set, the loop waits for the next occurrence
///         from the schedule before the first pass and between passes instead of waiting
///         <see cref="RecurringCommandHostOptions.Interval" />. A pass that fails still waits
///         <see cref="RecurringCommandHostOptions.ErrorBackoff" />, and a schedule with no further occurrence ends the
///         loop. Each occurrence the loop runs is recorded through <see cref="IRecurringOccurrenceStore" />, and
///         occurrences that passed without running are logged with their count; see
///         <see cref="RecurringCommandHostOptions.MissedOccurrencePolicy" /> for what runs next.
///     </para>
///     <para>
///         A tenant sweep with <see cref="RecurringCommandHostOptions.TenantSchedule" /> set waits through the
///         per-tenant occurrence provider instead: the loop waits for the earliest occurrence across the sweep's
///         tenants, and the sweep sends only for the tenants whose occurrence is due. The sweep owns its
///         per-tenant occurrence records, so the loop records nothing while a provider is set.
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
    ///     Computes the next occurrence a tenant sweep waits for, or null to wait on the shared schedule or the interval.
    /// </summary>
    private readonly Func<DateTimeOffset, CancellationToken, ValueTask<DateTimeOffset?>>? _getNextOccurrenceAsync;

    /// <summary>
    ///     Indicates whether the pass acquires one lease per tenant instead of one lease for the whole pass.
    /// </summary>
    private readonly bool _perTenantLeasing;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandLoop" /> class.
    /// </summary>
    /// <param name="options">The lifecycle and timing settings for this loop.</param>
    /// <param name="scopeFactory">Opens the scope the pass and the lease are resolved from.</param>
    /// <param name="timeProvider">Supplies the delay between passes.</param>
    /// <param name="logger">Records pass failures and lifecycle transitions.</param>
    /// <param name="displayName">The name the log messages use for this loop.</param>
    /// <param name="defaultLeaseName">The lease name used when the options name none.</param>
    /// <param name="getNextOccurrenceAsync">
    ///     Computes the next occurrence a tenant sweep waits for from the supplied instant, or
    ///     <see langword="null" /> to wait on the shared schedule or the interval.
    /// </param>
    /// <param name="perTenantLeasing">
    ///     <see langword="true" /> when the pass acquires one lease per tenant; the loop then resolves the lease
    ///     service but never acquires the loop lease itself.
    /// </param>
    public RecurringCommandLoop(
        RecurringCommandHostOptions options,
        IMessageDispatchScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger logger,
        string displayName,
        string defaultLeaseName,
        Func<DateTimeOffset, CancellationToken, ValueTask<DateTimeOffset?>>? getNextOccurrenceAsync = null,
        bool perTenantLeasing = false)
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
        _getNextOccurrenceAsync = getNextOccurrenceAsync;
        _perTenantLeasing = perTenantLeasing;
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
        var schedule = _options.Schedule;
        var missedPolicy = _options.MissedOccurrencePolicy;
        var occurrenceKey = _options.OccurrenceKey ?? DefaultRecurringOccurrenceKey.Instance;
        var occurrenceProvider = _getNextOccurrenceAsync;
        var loopName = _defaultLeaseName;

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

        IRecurringOccurrenceStore? occurrenceStore = null;
        if (occurrenceProvider is null && schedule is not null)
        {
            occurrenceStore = (IRecurringOccurrenceStore?) loopScope.ServiceProvider.GetService(typeof(IRecurringOccurrenceStore));

            if (occurrenceStore is null)
            {
                occurrenceStore = new InMemoryRecurringOccurrenceStore();
                RecurringCommandLogMessages.OccurrenceStoreFallback(_logger, _displayName);
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

        DateTimeOffset? currentTarget = null;
        var runFirstPassImmediately = false;

        if (occurrenceProvider is not null)
        {
            // A tenant-scheduled sweep aligns to per-tenant wall-clock occurrences from the start: the first pass
            // waits for the earliest occurrence across its tenants instead of running right after the startup delay.
            DateTimeOffset? first;

            try
            {
                first = await GetProviderOccurrenceWithRetryAsync(occurrenceProvider, errorBackoff, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (first is null)
            {
                return;
            }

            currentTarget = first;
        }
        else if (schedule is not null && occurrenceStore is not null)
        {
            ISchedule activeSchedule = schedule;

            // A scheduled loop aligns to wall-clock occurrences from the start: the first pass waits for the first
            // occurrence instead of running right after the startup delay, unless a recent miss is owed a catch-up.
            DateTimeOffset? last;

            try
            {
                last = await ReadLastScheduledOccurrenceAsync(occurrenceStore, loopName, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var basis = _timeProvider.GetUtcNow();
            var plan = await RecurringOccurrencePlanner.PlanAsync(
                    (after, _) => new ValueTask<DateTimeOffset?>(activeSchedule.GetNextOccurrence(after)),
                    last,
                    basis,
                    missedPolicy,
                    stoppingToken)
                .ConfigureAwait(false);

            LogMissedPlan(plan);

            if (plan.CatchUp is not null)
            {
                RecurringCommandLogMessages.CatchUpPassFired(_logger, _displayName, plan.CatchUp.Value);
                runFirstPassImmediately = true;
                currentTarget = plan.CatchUp;
            }
            else
            {
                if (plan.Next is null)
                {
                    return;
                }

                currentTarget = plan.Next;
            }
        }

        if (!runFirstPassImmediately
            && (occurrenceProvider is not null || schedule is not null)
            && currentTarget is not null)
        {
            var now = _timeProvider.GetUtcNow();
            var firstWait = currentTarget.Value - now;

            if (firstWait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(firstWait, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        IRecurringCommandLeaseHandle? lease = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var useBackoff = false;
                var passRan = false;

                try
                {
                    if (leaseService is not null && lease is null && !_perTenantLeasing)
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
                        useBackoff = true;
                    }
                    else if (leaseService is null || lease is not null || _perTenantLeasing)
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

                        passRan = true;

                        if (lease is not null && lease.LeaseLost.IsCancellationRequested)
                        {
                            // Lost mid-pass: the busy period ends even when the result reports more work.
                            RecurringCommandLogMessages.LeaseLost(_logger, _displayName, leaseName);
                            useBackoff = true;
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
                    useBackoff = true;
                }
                catch (RecurringPassBackoffException)
                {
                    // The pass already logged why it ended; it asks for the error backoff without being a fault.
                    passRan = true;
                    useBackoff = true;
                }
                catch (Exception exception) when (isConcurrencyConflict?.Invoke(exception) == true)
                {
                    // Another instance won the race for the same data, so the work this send wanted is already done or
                    // will be retried by the next pass. Waiting out the error backoff would only delay that pass.
                    passRan = true;
                    RecurringCommandLogMessages.PassConflicted(_logger, _displayName, exception.GetType().Name);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception exception)
                {
                    // A recurring loop must outlive the failures of what it calls; ending here would silently stop the
                    // work for the life of the process, which is the failure this catch exists to prevent.
                    passRan = true;
                    RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);
                    useBackoff = true;
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

                    if (occurrenceStore is not null && passRan && currentTarget is not null)
                    {
                        var key = occurrenceKey.CreateKey(loopName, null, currentTarget.Value);
                        await occurrenceStore.TryRecordAsync(
                                new RecurringOccurrence(
                                    loopName,
                                    null,
                                    key,
                                    currentTarget.Value,
                                    _timeProvider.GetUtcNow()),
                                stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    // Releasing the lease and recording the occurrence are part of the pass: a store that dropped the
                    // holding connection reports the failure here and backs off, rather than ending the loop.
                    lease = null;
                    RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);
                    useBackoff = true;
                }
#pragma warning restore CA1031

                TimeSpan wait;

                if (useBackoff)
                {
                    wait = errorBackoff;
                }
                else if (occurrenceProvider is null && schedule is null)
                {
                    wait = interval;
                }
                else if (occurrenceProvider is not null)
                {
                    DateTimeOffset? next;

                    try
                    {
                        next = await GetProviderOccurrenceWithRetryAsync(occurrenceProvider, errorBackoff, stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (next is null)
                    {
                        return;
                    }

                    wait = next.Value - _timeProvider.GetUtcNow();
                }
                else if (schedule is not null && occurrenceStore is not null)
                {
                    ISchedule activeSchedule = schedule;

                    var basis = _timeProvider.GetUtcNow();
                    var plan = await RecurringOccurrencePlanner.PlanAsync(
                            (after, _) => new ValueTask<DateTimeOffset?>(activeSchedule.GetNextOccurrence(after)),
                            currentTarget,
                            basis,
                            missedPolicy,
                            stoppingToken)
                        .ConfigureAwait(false);

                    LogMissedPlan(plan);

                    if (plan.CatchUp is not null)
                    {
                        RecurringCommandLogMessages.CatchUpPassFired(_logger, _displayName, plan.CatchUp.Value);
                        currentTarget = plan.CatchUp;
                        continue;
                    }

                    if (plan.Next is null)
                    {
                        return;
                    }

                    currentTarget = plan.Next;
                    wait = plan.Next.Value - _timeProvider.GetUtcNow();
                }
                else
                {
                    // Unreachable: a set schedule always resolves an occurrence store above. The interval keeps the
                    // loop paced rather than ending it, if that ever stops holding.
                    wait = interval;
                }

                if (wait <= TimeSpan.Zero)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(wait, _timeProvider, stoppingToken).ConfigureAwait(false);
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

    /// <summary>
    ///     Reads the last recorded scheduled occurrence for the loop, or null when none was recorded or the read fails.
    /// </summary>
    /// <param name="occurrenceStore">The store holding recorded occurrences.</param>
    /// <param name="loopName">The name identifying this loop in the store.</param>
    /// <param name="stoppingToken">The token that stops the loop when the host shuts down.</param>
    /// <returns>The scheduled instant of the last recorded occurrence, or <see langword="null" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="occurrenceStore" /> is <see langword="null" />.</exception>
    private async Task<DateTimeOffset?> ReadLastScheduledOccurrenceAsync(
        IRecurringOccurrenceStore occurrenceStore,
        string loopName,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceStore);

        try
        {
            var last = await occurrenceStore.GetLastAsync(loopName, null, stoppingToken).ConfigureAwait(false);

            return last?.ScheduledAt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception exception)
        {
            // A store that is unreachable at startup must not end the loop before its first pass; the miss is
            // logged and the loop waits for the next occurrence after now instead of evaluating a catch-up.
            RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);

            return null;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Asks the per-tenant occurrence provider for the next occurrence, retrying after the error backoff while it fails.
    /// </summary>
    /// <param name="occurrenceProvider">Computes the next occurrence the sweep waits for.</param>
    /// <param name="errorBackoff">The delay applied after a failure, instead of the normal wait.</param>
    /// <param name="stoppingToken">The token that stops the loop when the host shuts down.</param>
    /// <returns>The next occurrence, or <see langword="null" /> when every tenant schedule ended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="occurrenceProvider" /> is <see langword="null" />.</exception>
    private async Task<DateTimeOffset?> GetProviderOccurrenceWithRetryAsync(
        Func<DateTimeOffset, CancellationToken, ValueTask<DateTimeOffset?>> occurrenceProvider,
        TimeSpan errorBackoff,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceProvider);

        while (true)
        {
            try
            {
                return await occurrenceProvider(_timeProvider.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // A wait that ends because tenant enumeration failed is a fault like a failed pass: it is logged
                // and retried after the error backoff instead of ending the loop.
                RecurringCommandLogMessages.PassFailed(_logger, _displayName, exception);

                try
                {
                    await Task.Delay(errorBackoff, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Logs the missed occurrences one evaluation counted.
    /// </summary>
    /// <param name="plan">The evaluated plan.</param>
    private void LogMissedPlan(RecurringOccurrencePlan plan)
    {
        if (plan.MissedCount <= 0)
        {
            return;
        }

        if (plan.Truncated)
        {
            RecurringCommandLogMessages.TruncatedMissedOccurrences(_logger, _displayName, plan.MissedCount);
        }
        else
        {
            RecurringCommandLogMessages.MissedOccurrences(_logger, _displayName, plan.MissedCount);
        }
    }
}
