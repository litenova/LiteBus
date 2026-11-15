using System;
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
///         undefended. The in-flight send runs under a token linked to the loss signal, so a handler that observes
///         cancellation stops with the pass.
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
    ///     Opens the scope used to resolve the mediator once when the loop starts.
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
    /// <param name="scopeFactory">Opens the scope used to resolve the mediator once when the loop starts.</param>
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
    ///     Resolves the command mediator from the loop's scope and returns the pass that sends one command.
    /// </summary>
    /// <param name="serviceProvider">The service provider of the loop's scope.</param>
    /// <returns>The pass, which reports whether the result asked for more work.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">No <see cref="ICommandMediator" /> is registered.</exception>
    private Func<CancellationToken, Task<bool>> CreatePass(IServiceProvider serviceProvider)
    {
        var mediator = (ICommandMediator?) serviceProvider.GetService(typeof(ICommandMediator));
        if (mediator is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ICommandMediator));
        }

        return cancellationToken => SendOnceAsync(mediator, cancellationToken);
    }

    /// <summary>
    ///     Sends the command once through the mediator resolved when the loop started.
    /// </summary>
    /// <param name="mediator">The command mediator resolved once when the loop started.</param>
    /// <param name="cancellationToken">A token that stops the send when the host shuts down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mediator" /> is <see langword="null" />.</exception>
    /// <returns>
    ///     <see langword="true" /> when the result reported more work through <see cref="IRecurringWorkReport" />.
    /// </returns>
    private async Task<bool> SendOnceAsync(ICommandMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
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
