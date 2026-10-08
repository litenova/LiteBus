using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Sends one command on a fixed interval for the life of the host.
/// </summary>
/// <typeparam name="TCommand">The command type sent on each pass.</typeparam>
/// <remarks>
///     <para>
///         One instance per registered recurring command. Each pass opens its own dispatch scope through the mediator,
///         so a handler resolves scoped dependencies exactly as it would for a command sent from an endpoint.
///     </para>
///     <para>
///         A pass that throws is logged and the loop continues after
///         <see cref="RecurringCommandHostOptions.ErrorBackoff" />. An exception never escapes the loop, because a
///         background service that ends on the first transient database blip is a scheduled outage.
///     </para>
/// </remarks>
public sealed class RecurringCommandBackgroundService<TCommand> : IBackgroundService
    where TCommand : notnull
{
    /// <summary>
    ///     Produces the command instance sent on each pass.
    /// </summary>
    private readonly Func<TCommand> _commandFactory;

    /// <summary>
    ///     The lifecycle and timing settings for this loop.
    /// </summary>
    private readonly RecurringCommandHostOptions _options;

    /// <summary>
    ///     Opens one dispatch scope per pass so each send resolves its own scoped dependencies.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _scopeFactory;

    /// <summary>
    ///     Records pass failures and lifecycle transitions.
    /// </summary>
    private readonly ILogger<RecurringCommandBackgroundService<TCommand>> _logger;

    /// <summary>
    ///     Supplies the delay between passes.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandBackgroundService{TCommand}" /> class.
    /// </summary>
    /// <param name="declaration">The registered declaration carrying the factory and the host options.</param>
    /// <param name="scopeFactory">Opens one dispatch scope per pass.</param>
    /// <param name="timeProvider">Supplies the delay between passes.</param>
    /// <param name="logger">Records pass failures and lifecycle transitions, or null to discard them.</param>
    public RecurringCommandBackgroundService(
        RecurringCommandDeclaration<TCommand> declaration,
        IMessageDispatchScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RecurringCommandBackgroundService<TCommand>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _commandFactory = declaration.CommandFactory;
        _options = declaration.HostOptions;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<RecurringCommandBackgroundService<TCommand>>.Instance;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();

        if (!_options.Enabled)
        {
            RecurringCommandLogMessages.LoopDisabled(_logger, typeof(TCommand).Name);
            return;
        }

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

        RecurringCommandLogMessages.LoopStarted(_logger, typeof(TCommand).Name, _options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = _options.Interval;

            try
            {
                var hasMoreWork = await SendOnceAsync(stoppingToken).ConfigureAwait(false);

                if (hasMoreWork)
                {
                    // The pass did as much as it was allowed to, so the next one starts without waiting. This is what
                    // turns a large backlog into one busy period instead of one batch per interval.
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (_options.IsConcurrencyConflict?.Invoke(exception) == true)
            {
                // Another instance won the race for the same data, so the work this send wanted is already done or
                // will be retried by the next pass. Waiting out the error backoff would only delay that pass.
                RecurringCommandLogMessages.PassConflicted(_logger, typeof(TCommand).Name, exception.GetType().Name);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // A recurring loop must outlive the failures of what it calls; ending here would silently stop the
                // work for the life of the process, which is the failure this catch exists to prevent.
                RecurringCommandLogMessages.PassFailed(_logger, typeof(TCommand).Name, exception);
                delay = _options.ErrorBackoff;
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

    /// <summary>
    ///     Sends the command once inside its own dispatch scope.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the send when the host shuts down.</param>
    /// <returns>
    ///     <see langword="true" /> when the result reported more work through <see cref="IRecurringWorkReport" />.
    /// </returns>
    private async Task<bool> SendOnceAsync(CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateScope();
        await using var configuredScope = scope.ConfigureAwait(false);

        var mediator = (ICommandMediator) scope.ServiceProvider.GetService(typeof(ICommandMediator))!;
        var command = _commandFactory();

        // ICommand<TResult> derives from ICommand, so the result-bearing contract has to be tested first or every
        // command would take the no-result overload and its report would be thrown away.
        var resultContract = RecurringCommandContracts.FindResultContract(command.GetType());

        if (resultContract is null)
        {
            if (command is not ICommand plainCommand)
            {
                throw new InvalidOperationException(
                    $"'{typeof(TCommand).Name}' is registered as a recurring command but implements neither "
                    + "ICommand nor ICommand<TCommandResult>.");
            }

            await mediator.SendAsync(plainCommand, cancellationToken: cancellationToken).ConfigureAwait(false);
            return false;
        }

        var result = await RecurringCommandContracts
            .SendAsync(mediator, command, resultContract, cancellationToken)
            .ConfigureAwait(false);

        return result is IRecurringWorkReport { HasMoreWork: true };
    }
}
