using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Provides extension methods for <see cref="ICommandMediator" /> to simplify command sending operations.
/// </summary>
public static class CommandMediatorExtensions
{
    /// <summary>
    ///     Sends a command asynchronously using the specified command mediator.
    /// </summary>
    /// <param name="commandMediator">The command mediator to use for sending the command.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    /// <example>
    ///     Usage example:
    ///     <code>
    /// await commandMediator.SendAsync(myCommand, cancellationToken);
    /// </code>
    /// </example>
    public static Task SendAsync(this ICommandMediator commandMediator,
                                 ICommand command,
                                 CancellationToken cancellationToken = default)
    {
        return commandMediator.SendAsync(command, null, cancellationToken);
    }

    /// <summary>
    ///     Sends a command asynchronously using the specified command mediator and returns a result.
    /// </summary>
    /// <typeparam name="TCommandResult">The type of the result returned by the command.</typeparam>
    /// <param name="commandMediator">The command mediator to use for sending the command.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the result of the command.</returns>
    /// <example>
    ///     Usage example:
    ///     <code>
    /// var result = await commandMediator.SendAsync(myCommand, cancellationToken);
    /// </code>
    /// </example>
    public static Task<TCommandResult?> SendAsync<TCommandResult>(this ICommandMediator commandMediator,
                                                                  ICommand<TCommandResult> command,
                                                                  CancellationToken cancellationToken = default)
    {
        return commandMediator.SendAsync(command, null, cancellationToken);
    }

    /// <summary>
    ///     Sends a tagged command asynchronously using the specified command mediator.
    /// </summary>
    /// <param name="commandMediator">The command mediator to use for sending the command.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="tag">A tag that specifies the context or category of the command.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    /// <example>
    ///     Usage example:
    ///     <code>
    /// await commandMediator.SendAsync(myCommand, "UserAction", cancellationToken);
    /// </code>
    /// </example>
    public static Task SendAsync(this ICommandMediator commandMediator,
                                 ICommand command,
                                 string tag,
                                 CancellationToken cancellationToken = default)
    {
        return commandMediator.SendAsync(command,
            new CommandMediationSettings
            {
                Filters =
                {
                    Tags = [tag]
                }
            },
            cancellationToken);
    }

    /// <summary>
    ///     Sends a tagged command asynchronously using the specified command mediator and returns a result.
    /// </summary>
    /// <typeparam name="TCommandResult">The type of the result returned by the command.</typeparam>
    /// <param name="commandMediator">The command mediator to use for sending the command.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="tag">A tag that specifies the context or category of the command.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the result of the command.</returns>
    /// <example>
    ///     Usage example:
    ///     <code>
    /// var result = await commandMediator.SendAsync(myCommand, "UserAction", cancellationToken);
    /// </code>
    /// </example>
    public static Task<TCommandResult?> SendAsync<TCommandResult>(this ICommandMediator commandMediator,
                                                                  ICommand<TCommandResult> command,
                                                                  string tag,
                                                                  CancellationToken cancellationToken = default)
    {
        return commandMediator.SendAsync(command,
            new CommandMediationSettings
            {
                Filters =
                {
                    Tags = [tag]
                }
            },
            cancellationToken);
    }
}