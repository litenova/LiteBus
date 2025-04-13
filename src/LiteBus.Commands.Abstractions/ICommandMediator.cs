using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
/// Represents the mediator interface for sending commands within the application.
/// </summary>
/// <remarks>
/// The command mediator is responsible for routing commands to their appropriate handlers
/// and orchestrating the command handling pipeline. It ensures that commands are processed
/// by exactly one handler and provides methods for sending commands both with and without
/// expected results.
/// 
/// In the CQRS pattern, commands represent intentions to change the system state. The command
/// mediator helps maintain separation between the command issuers and the command handlers.
/// </remarks>
public interface ICommandMediator
{
    /// <summary>
    /// Asynchronously sends a command for mediation.
    /// </summary>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation that control aspects such as handler filtering.</param>
    /// <param name="cancellationToken">Cancellation token for the operation that can be used to cancel the command processing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method is used for commands that do not produce a result. The command is routed to its
    /// appropriate handler based on its type, and the command handling pipeline is executed, including
    /// pre-handlers, the main handler, post-handlers, and error handlers if exceptions occur.
    /// </remarks>
    Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously sends a command for mediation and returns a result.
    /// </summary>
    /// <typeparam name="TCommandResult">The type of the result returned by the command.</typeparam>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation that control aspects such as handler filtering.</param>
    /// <param name="cancellationToken">Cancellation token for the operation that can be used to cancel the command processing.</param>
    /// <returns>A task representing the asynchronous operation with a result of type <typeparamref name="TCommandResult"/>.</returns>
    /// <remarks>
    /// This method is used for commands that produce a result of type <typeparamref name="TCommandResult"/>.
    /// The command is routed to its appropriate handler based on its type, and the command handling pipeline
    /// is executed, including pre-handlers, the main handler, post-handlers, and error handlers if exceptions occur.
    /// The result produced by the handler is returned to the caller.
    /// </remarks>
    Task<TCommandResult?> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                    CommandMediationSettings? commandMediationSettings = null,
                                                    CancellationToken cancellationToken = default);
}