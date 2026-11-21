using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Sends one command per tenant on a fixed interval for the life of the host.
/// </summary>
/// <typeparam name="TCommand">The command type sent once per tenant on each pass.</typeparam>
/// <remarks>
///     <para>
///         One instance per registered sweep. Each pass enumerates the tenants from the sweep's
///         <see cref="IRecurringTenantSource" />, builds one command per tenant, and sends it with
///         <see cref="ExecutionIdentity.ForTenant" />. A source registered by type or through dependency injection is
///         resolved from a fresh dispatch scope on every pass, so it takes scoped dependencies exactly as a handler
///         would, and the tenant identifiers are read before any command is sent.
///     </para>
///     <para>
///         Each source read runs under <see cref="RecurringCommandHostOptions.SourceIdentity" /> inside a root
///         mediation scope, so a query the source sends inherits that identity through the existing
///         enclosing-mediation rule instead of running anonymously.
///     </para>
///     <para>
///         The sweep always sends one mediation per tenant, like a batch with
///         <see cref="RecurringBatchIsolation.PerItem" />: one tenant's command failing or refused is logged and
///         counted and never stops the rest of the sweep. A refused command is logged once at Warning, a failure
///         <see cref="RecurringCommandHostOptions.IsConcurrencyConflict" /> classifies as a lost race is logged at
///         Information, and any other failure is logged at Error. When any tenant failed or was refused, the pass asks
///         for <see cref="RecurringCommandHostOptions.ErrorBackoff" /> through
///         <see cref="RecurringPassBackoffException" />, so a tenant refused on every pass does not spin the loop.
///         Otherwise the pass reports whether any command asked for more work through
///         <see cref="IRecurringWorkReport" />. An exception never escapes the loop, because a background service that
///         ends on the first transient database blip is a scheduled outage.
///     </para>
///     <para>
///         With <see cref="RecurringCommandHostOptions.TenantSchedule" /> set, the sweep waits for the earliest
///         occurrence across its tenants instead of the shared schedule or the interval, and each pass sends only
///         for the tenants whose occurrence is due. Each occurrence a tenant runs is recorded through
///         <see cref="IRecurringOccurrenceStore" />, and occurrences that passed without running are logged with
///         their count; see <see cref="RecurringCommandHostOptions.MissedOccurrencePolicy" /> for what runs next.
///     </para>
///     <para>
///         When <see cref="RecurringCommandHostOptions.RequireLease" /> is set with
///         <see cref="RecurringLeaseScope.Loop" />, each pass first acquires the named
///         <see cref="IRecurringCommandLease" />: a pass that cannot acquire it is skipped and the loop waits
///         <see cref="RecurringCommandHostOptions.Interval" />. One acquired lease covers the whole busy period, so
///         immediate re-sends never let another replica interleave mid-backlog, and it is released before the loop
///         waits. Releasing the lease is part of the pass, so a release failure is logged and backed off like any
///         other pass failure.
///     </para>
///     <para>
///         With <see cref="RecurringLeaseScope.Tenant" /> or
///         <see cref="RecurringLeaseScope.TenantOccurrence" />, the pass instead acquires one lease per tenant
///         around that tenant's send: a tenant whose lease another instance holds is skipped and the sweep
///         continues with the next tenant, so replicas divide tenants instead of contending for the whole sweep.
///         A per-tenant lease lost mid-send ends the pass with the error backoff, like a loop-level loss.
///     </para>
///     <para>
///         The lease can be lost mid-pass when another holder takes the row or renewals fail. The handle signals that
///         through <see cref="IRecurringCommandLeaseHandle.LeaseLost" />: the loop ends the busy period, releases the
///         handle, and waits <see cref="RecurringCommandHostOptions.ErrorBackoff" /> instead of sending further passes
///         undefended. The in-flight tenant read and sends run under a token linked to the loss signal, so work that
///         observes cancellation stops with the pass.
///     </para>
/// </remarks>
public sealed class RecurringTenantSweepBackgroundService<TCommand> : IBackgroundService
    where TCommand : notnull
{
    /// <summary>
    ///     Builds the command sent for one tenant.
    /// </summary>
    private readonly Func<string, TCommand> _commandFactory;

    /// <summary>
    ///     The tenant source each pass enumerates, or null when the loop resolves the source from a dispatch scope.
    /// </summary>
    private readonly IRecurringTenantSource? _tenantSource;

    /// <summary>
    ///     The tenant source type each pass resolves, or null when it carries an instance or resolves the port itself.
    /// </summary>
    private readonly Type? _tenantSourceType;

    /// <summary>
    ///     The lifecycle and timing settings for this loop.
    /// </summary>
    private readonly RecurringCommandHostOptions _options;

    /// <summary>
    ///     Recognizes an optimistic-concurrency conflict thrown by a tenant send, or null to treat every exception as a
    ///     failure.
    /// </summary>
    private readonly Func<Exception, bool>? _isConcurrencyConflict;

    /// <summary>
    ///     Decides when the sweep's passes run per tenant, or null to use the shared schedule or the interval.
    /// </summary>
    private readonly ITenantRecurringSchedule? _tenantSchedule;

    /// <summary>
    ///     Chooses what one acquired lease single-flights: the whole loop, one tenant, or one scheduled occurrence
    ///     of one tenant.
    /// </summary>
    private readonly RecurringLeaseScope _leaseScope;

    /// <summary>
    ///     Decides what the sweep does about occurrences it missed while down or busy.
    /// </summary>
    private readonly RecurringMissedOccurrencePolicy _missedPolicy;

    /// <summary>
    ///     The identity tenant source reads run under.
    /// </summary>
    private readonly ExecutionIdentity _sourceIdentity;

    /// <summary>
    ///     Builds the stable key identifying one scheduled occurrence.
    /// </summary>
    private readonly IRecurringOccurrenceKey _occurrenceKey;

    /// <summary>
    ///     Records the occurrences due tenants run, resolved once from the loop's scope when the loop starts.
    /// </summary>
    private IRecurringOccurrenceStore? _occurrenceStore;

    /// <summary>
    ///     The delay between passes when the sweep enumerated no tenants.
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    ///     The delay applied after a failure, instead of the normal wait.
    /// </summary>
    private readonly TimeSpan _errorBackoff;

    /// <summary>
    ///     The name identifying this sweep in the occurrence store and the default lease name.
    /// </summary>
    private readonly string _loopName;

    /// <summary>
    ///     The pending occurrence per tenant from the latest wait computation, used by the pass to decide who is due.
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset?> _pendingOccurrences = new(StringComparer.Ordinal);

    /// <summary>
    ///     The tenant occurrences fired since the latest wait computation, so a busy pass re-sends without re-recording.
    /// </summary>
    private readonly HashSet<(string TenantId, DateTimeOffset Occurrence)> _firedOccurrences = [];

    /// <summary>
    ///     Records occurrences when no <see cref="IRecurringOccurrenceStore" /> is registered.
    /// </summary>
    private readonly InMemoryRecurringOccurrenceStore _fallbackStore = new();

    /// <summary>
    ///     Indicates whether the occurrence store fallback was already logged.
    /// </summary>
    private bool _fallbackLogged;

    /// <summary>
    ///     Opens the loop scope once when the loop starts, and a fresh scope for the tenant source on each pass.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _scopeFactory;

    /// <summary>
    ///     Supplies the delay between passes.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Records pass failures and lifecycle transitions.
    /// </summary>
    private readonly ILogger<RecurringTenantSweepBackgroundService<TCommand>> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringTenantSweepBackgroundService{TCommand}" /> class.
    /// </summary>
    /// <param name="declaration">The registered declaration carrying the factory, the tenant source, and the options.</param>
    /// <param name="scopeFactory">Opens the loop scope once, and a fresh scope for the tenant source on each pass.</param>
    /// <param name="timeProvider">Supplies the delay between passes.</param>
    /// <param name="logger">Records pass failures and lifecycle transitions, or null to discard them.</param>
    public RecurringTenantSweepBackgroundService(
        RecurringTenantSweepDeclaration<TCommand> declaration,
        IMessageDispatchScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RecurringTenantSweepBackgroundService<TCommand>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _commandFactory = declaration.CommandFactory;
        _tenantSource = declaration.TenantSource;
        _tenantSourceType = declaration.TenantSourceType;
        _options = declaration.HostOptions;
        _isConcurrencyConflict = declaration.HostOptions.IsConcurrencyConflict;
        _tenantSchedule = declaration.HostOptions.TenantSchedule;
        _leaseScope = declaration.HostOptions.LeaseScope;
        _missedPolicy = declaration.HostOptions.MissedOccurrencePolicy;
        _sourceIdentity = declaration.HostOptions.SourceIdentity;
        _occurrenceKey = declaration.HostOptions.OccurrenceKey ?? DefaultRecurringOccurrenceKey.Instance;
        _interval = declaration.HostOptions.Interval;
        _errorBackoff = declaration.HostOptions.ErrorBackoff;
        _loopName = typeof(TCommand).FullName ?? typeof(TCommand).Name;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<RecurringTenantSweepBackgroundService<TCommand>>.Instance;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenantSchedule = _tenantSchedule;
        var perTenantLeasing = _options.RequireLease && _leaseScope != RecurringLeaseScope.Loop;

        var loop = new RecurringCommandLoop(
            _options,
            _scopeFactory,
            _timeProvider,
            _logger,
            typeof(TCommand).Name,
            _loopName,
            getNextOccurrenceAsync: tenantSchedule is null
                ? null
                : (basis, cancellationToken) => ComputeNextSweepOccurrenceAsync(tenantSchedule, basis, cancellationToken),
            perTenantLeasing: perTenantLeasing);

        return loop.RunAsync(CreatePass, stoppingToken);
    }

    /// <summary>
    ///     Resolves the command mediator, the occurrence store, and the lease service from the loop's scope, and
    ///     returns the pass that sweeps every tenant.
    /// </summary>
    /// <param name="serviceProvider">The service provider of the loop's scope.</param>
    /// <returns>The pass, which reports whether any tenant asked for more work.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">
    ///     No <see cref="ICommandMediator" /> is registered, or no <see cref="IRecurringCommandLease" /> is
    ///     registered while the sweep leases per tenant.
    /// </exception>
    private Func<CancellationToken, Task<bool>> CreatePass(IServiceProvider serviceProvider)
    {
        var mediator = (ICommandMediator?) serviceProvider.GetService(typeof(ICommandMediator));
        if (mediator is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ICommandMediator));
        }

        IRecurringCommandLease? leaseService = null;
        if (_options.RequireLease && _leaseScope != RecurringLeaseScope.Loop)
        {
            leaseService = (IRecurringCommandLease?) serviceProvider.GetService(typeof(IRecurringCommandLease));

            if (leaseService is null)
            {
                throw new LiteBusDependencyResolutionException(typeof(IRecurringCommandLease));
            }
        }

        IRecurringOccurrenceStore? occurrenceStore = null;
        if (_tenantSchedule is not null)
        {
            occurrenceStore = (IRecurringOccurrenceStore?) serviceProvider.GetService(typeof(IRecurringOccurrenceStore))
                ?? UseFallbackStore();
            _occurrenceStore = occurrenceStore;
        }

        return cancellationToken => SendSweepOnceAsync(mediator, occurrenceStore, leaseService, cancellationToken);
    }

    /// <summary>
    ///     Computes the next occurrence the sweep waits for: the earliest pending occurrence across its tenants.
    /// </summary>
    /// <param name="tenantSchedule">Decides when the sweep's passes run per tenant.</param>
    /// <param name="basis">The instant the evaluation is computed for.</param>
    /// <param name="cancellationToken">A token that stops the evaluation when the host shuts down.</param>
    /// <returns>
    ///     The earliest pending occurrence, the interval when the sweep enumerated no tenants, or
    ///     <see langword="null" /> when every tenant schedule ended.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="tenantSchedule" /> is <see langword="null" />.</exception>
    private async ValueTask<DateTimeOffset?> ComputeNextSweepOccurrenceAsync(
        ITenantRecurringSchedule tenantSchedule,
        DateTimeOffset basis,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantSchedule);

        var tenants = await ListTenantsAsync(cancellationToken).ConfigureAwait(false);

        _pendingOccurrences.Clear();
        _firedOccurrences.Clear();

        if (tenants.Count == 0)
        {
            // Tenants may appear later, so an empty sweep retries after the interval instead of ending the loop.
            return basis + _interval;
        }

        // The pass factory resolved this from the loop's scope before the first wait; the fallback covers a
        // call that skipped it.
        var occurrenceStore = _occurrenceStore ?? UseFallbackStore();
        var commandName = typeof(TCommand).Name;
        DateTimeOffset? earliest = null;
        var missedTotal = 0;
        var missedTenants = 0;
        var catchUpTenants = 0;

        foreach (var tenant in tenants)
        {
            if (string.IsNullOrWhiteSpace(tenant))
            {
                // A blank identifier fails loudly in the pass; it never paces the loop.
                continue;
            }

            try
            {
                var last = await occurrenceStore.GetLastAsync(_loopName, tenant, cancellationToken).ConfigureAwait(false);
                var plan = await RecurringOccurrencePlanner.PlanAsync(
                        (after, token) => tenantSchedule.GetNextOccurrenceAsync(tenant, after, token),
                        last?.ScheduledAt,
                        basis,
                        _missedPolicy,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (plan.MissedCount > 0)
                {
                    missedTotal += plan.MissedCount;
                    missedTenants++;
                }

                var pending = plan.CatchUp ?? plan.Next;

                if (plan.CatchUp is not null)
                {
                    catchUpTenants++;
                }

                _pendingOccurrences[tenant] = pending;

                if (pending is not null && (earliest is null || pending.Value < earliest.Value))
                {
                    earliest = pending;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // One tenant's schedule lookup failing never stops the rest of the sweep's timing: it is logged
                // and the tenant sits this cycle out. When every lookup fails, the sweep retries after the error
                // backoff instead of ending the loop.
                RecurringCommandLogMessages.TenantCommandFailed(_logger, commandName, tenant, exception);
            }
#pragma warning restore CA1031
        }

        if (missedTotal > 0)
        {
            RecurringCommandLogMessages.SweepMissedOccurrences(_logger, commandName, missedTotal, missedTenants);
        }

        if (catchUpTenants > 0)
        {
            RecurringCommandLogMessages.SweepCatchUpPassesFired(_logger, commandName, catchUpTenants);
        }

        if (earliest is not null)
        {
            return earliest;
        }

        return _pendingOccurrences.Count == 0 ? basis + _errorBackoff : null;
    }

    /// <summary>
    ///     Reads the tenants one pass sends for, under the configured source identity.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the read when the host shuts down or the lease is lost.</param>
    /// <returns>The tenant identifiers, in the order the pass sends for them.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">No tenant source is registered.</exception>
    private async ValueTask<List<string>> ListTenantsAsync(CancellationToken cancellationToken)
    {
        List<string> tenants = [];

        if (_tenantSource is not null)
        {
            using (RootMediationScope.Begin())
            using (AmbientExecutionContext.CreateScope(
                       new RecurringSourceExecutionContext(_sourceIdentity, _timeProvider, cancellationToken)))
            {
                await foreach (var tenant in _tenantSource.GetTenantsAsync(cancellationToken).ConfigureAwait(false))
                {
                    tenants.Add(tenant);
                }
            }
        }
        else
        {
            var sourceScope = _scopeFactory.CreateScope();
            await using (sourceScope.ConfigureAwait(false))
            {
                var sourceType = _tenantSourceType ?? typeof(IRecurringTenantSource);
                var source = (IRecurringTenantSource?) sourceScope.ServiceProvider.GetService(sourceType);

                if (source is null)
                {
                    throw new LiteBusDependencyResolutionException(sourceType);
                }

                // The identifiers are read before any command is sent, because the scope they were read from is
                // disposed before the sweep sends.
                using (RootMediationScope.Begin())
                using (AmbientExecutionContext.CreateScope(
                           new RecurringSourceExecutionContext(_sourceIdentity, _timeProvider, cancellationToken)))
                {
                    await foreach (var tenant in source.GetTenantsAsync(cancellationToken).ConfigureAwait(false))
                    {
                        tenants.Add(tenant);
                    }
                }
            }
        }

        return tenants;
    }

    /// <summary>
    ///     Returns the process-local occurrence store and logs the fallback once.
    /// </summary>
    /// <returns>The process-local occurrence store.</returns>
    private InMemoryRecurringOccurrenceStore UseFallbackStore()
    {
        if (!_fallbackLogged)
        {
            _fallbackLogged = true;
            RecurringCommandLogMessages.OccurrenceStoreFallback(_logger, typeof(TCommand).Name);
        }

        return _fallbackStore;
    }

    /// <summary>
    ///     Reads the tenants and sends one command per tenant through the mediator resolved when the loop started.
    /// </summary>
    /// <param name="mediator">The command mediator resolved once when the loop started.</param>
    /// <param name="occurrenceStore">Records the occurrences due tenants run, or <see langword="null" /> without a tenant schedule.</param>
    /// <param name="leaseService">Acquires one lease per tenant, or <see langword="null" /> for a loop-level lease or none.</param>
    /// <param name="cancellationToken">A token that stops the pass when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     <see langword="true" /> when no tenant failed and a command reported more work through
    ///     <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="mediator" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusDependencyResolutionException">No tenant source is registered.</exception>
    private async Task<bool> SendSweepOnceAsync(
        ICommandMediator mediator,
        IRecurringOccurrenceStore? occurrenceStore,
        IRecurringCommandLease? leaseService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        var tenants = await ListTenantsAsync(cancellationToken).ConfigureAwait(false);

        if (tenants.Count == 0)
        {
            return false;
        }

        var scheduled = _tenantSchedule is not null;
        var useOccurrenceLease = leaseService is not null
            && _leaseScope == RecurringLeaseScope.TenantOccurrence
            && scheduled;
        var leaseBase = _options.LeaseName ?? _loopName;
        var commandName = typeof(TCommand).Name;
        var now = _timeProvider.GetUtcNow();
        var failures = 0;
        var skipped = 0;
        var hasMoreWork = false;

        for (var index = 0; index < tenants.Count; index++)
        {
            var tenant = tenants[index];
            var tenantName = string.IsNullOrWhiteSpace(tenant) ? "(blank)" : tenant;
            var pending = now;
            var counted = false;

            if (scheduled && !string.IsNullOrWhiteSpace(tenant))
            {
                if (!_pendingOccurrences.TryGetValue(tenant, out var due)
                    || due is null
                    || due.Value > now)
                {
                    skipped++;
                    continue;
                }

                pending = due.Value;
            }

            if (string.IsNullOrWhiteSpace(tenant))
            {
                RecurringCommandLogMessages.TenantCommandFailed(
                    _logger,
                    commandName,
                    tenantName,
                    new InvalidOperationException(
                        $"The tenant source returned a blank tenant identifier at position {index}. "
                        + "A sweep sends with ExecutionIdentity.ForTenant, which needs one tenant per command."));
                failures++;
                continue;
            }

            IRecurringCommandLeaseHandle? handle = null;

            try
            {
                if (leaseService is not null)
                {
                    var leaseName = useOccurrenceLease
                        ? $"{leaseBase}/{_occurrenceKey.CreateKey(_loopName, tenant, pending)}"
                        : $"{leaseBase}/{tenant}";

                    handle = await leaseService.TryAcquireAsync(leaseName, cancellationToken).ConfigureAwait(false);

                    if (handle is null)
                    {
                        RecurringCommandLogMessages.TenantLeaseSkipped(_logger, commandName, tenant, leaseName);
                        continue;
                    }
                }

                var command = _commandFactory(tenant);
                var settings = new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant(tenant) };

                bool more;

                if (handle is null)
                {
                    more = await SendTenantCommandAsync(mediator, command, settings, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    using var loss = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        handle.LeaseLost);
                    more = await SendTenantCommandAsync(mediator, command, settings, loss.Token)
                        .ConfigureAwait(false);
                }

                hasMoreWork |= more;
            }
            catch (OperationCanceledException) when (handle is not null
                && handle.LeaseLost.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // The in-flight send observed lease loss through the linked token. The handle is released below
                // and the pass backs off, like a loop-level loss.
                RecurringCommandLogMessages.LeaseLost(_logger, commandName, $"{leaseBase}/{tenant}");

                throw new RecurringPassBackoffException(
                    $"The tenant sweep for '{commandName}' lost the lease for tenant '{tenant}' mid-send.");
            }
            catch (OperationCanceledException)
            {
                // Shutdown stops the pass; anything else is one tenant's failure and stays isolated.
                throw;
            }
            catch (RecurringPassBackoffException)
            {
                throw;
            }
            catch (Exception exception) when (_isConcurrencyConflict?.Invoke(exception) == true)
            {
                // Another instance won the race for this tenant's data, so the work is already done or will be
                // retried by the next pass. It is logged like a pass conflict and never backs the loop off.
                RecurringCommandLogMessages.PassConflicted(_logger, commandName, exception.GetType().Name);
            }
            catch (LiteBusMessageDeniedException denied)
            {
                RecurringCommandLogMessages.TenantCommandRefused(
                    _logger, commandName, tenantName, denied.Code ?? "(none)");
                failures++;
                counted = true;
            }
            catch (LiteBusMessageInvalidException invalid)
            {
                var code = invalid.Failures.Count == 0 ? "(none)" : invalid.Failures[0].Code ?? "(none)";
                RecurringCommandLogMessages.TenantCommandRefused(_logger, commandName, tenantName, code);
                failures++;
                counted = true;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // One failing tenant never stops the rest of the sweep: it is logged and counted here, and the pass
                // asks for the error backoff after the last tenant instead of failing the loop.
                RecurringCommandLogMessages.TenantCommandFailed(_logger, commandName, tenantName, exception);
                failures++;
                counted = true;
            }
#pragma warning restore CA1031
            finally
            {
                if (handle is not null)
                {
                    try
                    {
                        await handle.DisposeAsync().ConfigureAwait(false);
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch (Exception exception)
                    {
                        // Releasing the tenant lease is part of the tenant's work: a store that dropped the
                        // holding connection reports the failure here and counts the tenant as failed.
                        RecurringCommandLogMessages.TenantCommandFailed(_logger, commandName, tenantName, exception);

                        if (!counted)
                        {
                            failures++;
                        }
                    }
#pragma warning restore CA1031
                }
            }

            if (scheduled
                && occurrenceStore is not null
                && _firedOccurrences.Add((tenant, pending)))
            {
                try
                {
                    await occurrenceStore.TryRecordAsync(
                            new RecurringOccurrence(
                                _loopName,
                                tenant,
                                _occurrenceKey.CreateKey(_loopName, tenant, pending),
                                pending,
                                _timeProvider.GetUtcNow()),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception exception)
                {
                    // Recording the occurrence is part of the tenant's work: without the record the occurrence
                    // stays due and refires next cycle, so the failure is counted and the pass backs off.
                    RecurringCommandLogMessages.TenantCommandFailed(_logger, commandName, tenantName, exception);
                    failures++;
                }
#pragma warning restore CA1031
            }
        }

        if (scheduled && skipped > 0)
        {
            RecurringCommandLogMessages.SweepTenantsNotDue(_logger, commandName, skipped, tenants.Count);
        }

        if (failures > 0)
        {
            RecurringCommandLogMessages.SweepCompletedWithFailures(_logger, commandName, failures, tenants.Count);

            throw new RecurringPassBackoffException(
                $"The tenant sweep for '{commandName}' finished with {failures} of {tenants.Count} tenants failed.");
        }

        return hasMoreWork;
    }

    /// <summary>
    ///     Sends one tenant's command through the mediator resolved when the loop started.
    /// </summary>
    /// <param name="mediator">The command mediator resolved once when the loop started.</param>
    /// <param name="command">The command built for the tenant.</param>
    /// <param name="settings">The settings carrying the tenant's identity.</param>
    /// <param name="cancellationToken">A token that stops the send when the host shuts down.</param>
    /// <returns>
    ///     <see langword="true" /> when the result reported more work through <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="mediator" />, <paramref name="command" /> or <paramref name="settings" /> is
    ///     <see langword="null" />.
    /// </exception>
    private static async Task<bool> SendTenantCommandAsync(
        ICommandMediator mediator,
        TCommand command,
        CommandMediationSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(settings);

        // ICommand<TResult> derives from ICommand, so the result-bearing contract has to be tested first or every
        // command would take the no-result overload and its report would be thrown away.
        var resultContract = RecurringCommandContracts.FindResultContract(command.GetType());

        if (resultContract is null)
        {
            if (command is not ICommand plainCommand)
            {
                throw new InvalidOperationException(
                    $"'{typeof(TCommand).Name}' is registered as a recurring tenant sweep command but implements "
                    + "neither ICommand nor ICommand<TCommandResult>.");
            }

            await mediator.SendAsync(plainCommand, settings, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var result = await RecurringCommandContracts
            .SendAsync(mediator, command, resultContract, settings, cancellationToken)
            .ConfigureAwait(false);

        return result is IRecurringWorkReport { HasMoreWork: true };
    }
}
