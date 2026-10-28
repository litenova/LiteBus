using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the mediator interface for sending commands within the application.
/// </summary>
/// <remarks>
///     The command mediator is responsible for routing commands to their appropriate handlers
///     and orchestrating the command handling pipeline. It ensures that commands are processed
///     by exactly one handler and provides methods for sending commands both with and without
///     expected results.
///     In the CQRS pattern, commands represent intentions to change the system state. The command
///     mediator helps maintain separation between the command issuers and the command handlers.
/// </remarks>
public interface ICommandMediator
{
    /// <summary>
    ///     Asynchronously sends a command for mediation.
    /// </summary>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">
    ///     Optional settings for command mediation that control aspects such as handler
    ///     filtering.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation that can be used to cancel the command processing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    ///     This method is used for commands that do not produce a result. The command is routed to its
    ///     appropriate handler based on its type, and the command handling pipeline is executed, including
    ///     pre-handlers, the main handler, post-handlers, and error handlers if exceptions occur.
    /// </remarks>
    Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously sends a command for mediation and returns a result.
    /// </summary>
    /// <typeparam name="TCommandResult">The type of the result returned by the command.</typeparam>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">
    ///     Optional settings for command mediation that control aspects such as handler
    ///     filtering.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation that can be used to cancel the command processing.</param>
    /// <returns>A task representing the asynchronous operation with a result of type <typeparamref name="TCommandResult" />.</returns>
    /// <remarks>
    ///     This method is used for commands that produce a result of type <typeparamref name="TCommandResult" />.
    ///     The command is routed to its appropriate handler based on its type, and the command handling pipeline
    ///     is executed, including pre-handlers, the main handler, post-handlers, and error handlers if exceptions occur.
    ///     The result produced by the handler is returned to the caller.
    /// </remarks>
    Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                   CommandMediationSettings? commandMediationSettings = null,
                                                   CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously sends a command and returns how the mediation ended instead of raising a refusal.
    /// </summary>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The outcome, and the reason and code when a decision stopped the pipeline.</returns>
    /// <remarks>
    ///     <para>
    ///         A denial and a validation failure are routine endings, and this is the method for a boundary that
    ///         branches on them. <see cref="SendAsync(ICommand, CommandMediationSettings, CancellationToken)" />
    ///         converts both to exceptions, which leaves an HTTP endpoint catching one to produce a 403.
    ///     </para>
    ///     <para>
    ///         A genuine fault still throws. A database timeout is not something a boundary should branch on, so the
    ///         line is drawn where the pipeline already draws it: a decision is a value, a fault is an exception.
    ///     </para>
    /// </remarks>
    Task<MediationResult> TrySendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously sends a command and returns how the mediation ended, with the value when it produced one.
    /// </summary>
    /// <typeparam name="TCommandResult">The type of the result returned by the command.</typeparam>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The outcome and the value, or the reason and code when a decision stopped the pipeline.</returns>
    /// <remarks>
    ///     Read <see cref="MediationResult{TMessageResult}.IsSuccess" /> before the value: a refusal produces none,
    ///     unless an <c>ICommandRefusalMapper</c> is registered, in which case the mapped value arrives alongside the
    ///     denied outcome.
    /// </remarks>
    Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(
        ICommand<TCommandResult> command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asks the pipeline whether a command would be permitted and well-formed, without performing it.
    /// </summary>
    /// <param name="command">The command to evaluate.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The decision the guard and validator stages reached.</returns>
    /// <remarks>
    ///     <para>
    ///         This is what removes the second authorization method an application otherwise writes: one to authorize
    ///         while doing, and one for a caller that shows or hides a control. Two methods answering the same question
    ///         drift, and the drift is silent and security-relevant, because a button stays visible for an action the
    ///         pipeline will refuse. Now that authorization is declared on the command, the pipeline can be asked the
    ///         same question it will actually ask.
    ///     </para>
    ///     <para>
    ///         It runs the guard and validator stages only. It deliberately does not run shortcuts or pre-handlers,
    ///         because those act rather than decide: the shipped idempotency shortcut claims a key, so evaluating a
    ///         page full of controls would burn keys for commands nobody submitted.
    ///     </para>
    ///     <para>
    ///         That puts one obligation on a guard: it has to be free of effects a caller would not want from a "may
    ///         I" question. A guard that loads an aggregate to decide is fine, and the load happens on every
    ///         evaluation, so check the execution context data store before loading if it is expensive.
    ///     </para>
    /// </remarks>
    Task<MediationDecision> EvaluateAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default);
}
