using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
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
///         When <see cref="RecurringCommandHostOptions.RequireLease" /> is set, each pass first acquires the named
///         <see cref="IRecurringCommandLease" />: a pass that cannot acquire it is skipped and the loop waits
///         <see cref="RecurringCommandHostOptions.Interval" />. One acquired lease covers the whole busy period, so
///         immediate re-sends never let another replica interleave mid-backlog, and it is released before the loop
///         waits. Releasing the lease is part of the pass, so a release failure is logged and backed off like any
///         other pass failure.
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
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<RecurringTenantSweepBackgroundService<TCommand>>.Instance;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loop = new RecurringCommandLoop(
            _options,
            _scopeFactory,
            _timeProvider,
            _logger,
            typeof(TCommand).Name,
            typeof(TCommand).FullName ?? typeof(TCommand).Name);

        return loop.RunAsync(CreatePass, stoppingToken);
    }

    /// <summary>
    ///     Resolves the command mediator from the loop's scope and returns the pass that sweeps every tenant.
    /// </summary>
    /// <param name="serviceProvider">The service provider of the loop's scope.</param>
    /// <returns>The pass, which reports whether any tenant asked for more work.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">No <see cref="ICommandMediator" /> is registered.</exception>
    private Func<CancellationToken, Task<bool>> CreatePass(IServiceProvider serviceProvider)
    {
        var mediator = (ICommandMediator?) serviceProvider.GetService(typeof(ICommandMediator));
        if (mediator is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ICommandMediator));
        }

        return cancellationToken => SendSweepOnceAsync(mediator, cancellationToken);
    }

    /// <summary>
    ///     Reads the tenants and sends one command per tenant through the mediator resolved when the loop started.
    /// </summary>
    /// <param name="mediator">The command mediator resolved once when the loop started.</param>
    /// <param name="cancellationToken">A token that stops the pass when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     <see langword="true" /> when no tenant failed and a command reported more work through
    ///     <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="mediator" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusDependencyResolutionException">No tenant source is registered.</exception>
    private async Task<bool> SendSweepOnceAsync(ICommandMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        List<string> tenants = [];

        if (_tenantSource is not null)
        {
            await foreach (var tenant in _tenantSource.GetTenantsAsync(cancellationToken).ConfigureAwait(false))
            {
                tenants.Add(tenant);
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
                await foreach (var tenant in source.GetTenantsAsync(cancellationToken).ConfigureAwait(false))
                {
                    tenants.Add(tenant);
                }
            }
        }

        if (tenants.Count == 0)
        {
            return false;
        }

        var commandName = typeof(TCommand).Name;
        var failures = 0;
        var hasMoreWork = false;

        for (var index = 0; index < tenants.Count; index++)
        {
            var tenant = tenants[index];
            var tenantName = string.IsNullOrWhiteSpace(tenant) ? "(blank)" : tenant;

            try
            {
                if (string.IsNullOrWhiteSpace(tenant))
                {
                    throw new InvalidOperationException(
                        $"The tenant source returned a blank tenant identifier at position {index}. "
                        + "A sweep sends with ExecutionIdentity.ForTenant, which needs one tenant per command.");
                }

                var command = _commandFactory(tenant);
                var settings = new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant(tenant) };

                hasMoreWork |= await SendTenantCommandAsync(mediator, command, settings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown and lease loss stop the pass; anything else is one tenant's failure and stays isolated.
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
            }
            catch (LiteBusMessageInvalidException invalid)
            {
                var code = invalid.Failures.Count == 0 ? "(none)" : invalid.Failures[0].Code ?? "(none)";
                RecurringCommandLogMessages.TenantCommandRefused(_logger, commandName, tenantName, code);
                failures++;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // One failing tenant never stops the rest of the sweep: it is logged and counted here, and the pass
                // asks for the error backoff after the last tenant instead of failing the loop.
                RecurringCommandLogMessages.TenantCommandFailed(_logger, commandName, tenantName, exception);
                failures++;
            }
#pragma warning restore CA1031
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
