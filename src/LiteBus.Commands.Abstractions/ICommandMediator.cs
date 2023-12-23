#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
/// Represents the mediator interface for sending commands within the application.
/// </summary>
public interface ICommandMediator
{
    /// <summary>
    /// Asynchronously sends a command for mediation.
    /// </summary>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendAsync(ICommand command,
                   CommandMediationSettings? commandMediationSettings = null,
                   CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously sends a command for mediation and returns a result.
    /// </summary>
    /// <typeparam name="TCommandResult">The type of the result returned by the command.</typeparam>
    /// <param name="command">The command to be sent.</param>
    /// <param name="commandMediationSettings">Optional settings for command mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation with a result of type <typeparamref name="TCommandResult"/>.</returns>
    Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                   CommandMediationSettings? commandMediationSettings = null,
                                                   CancellationToken cancellationToken = default);
}