using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the definition of a handler that handles a command with result
/// </summary>
/// <typeparam name="TCommand">The type of command to</typeparam>
/// <typeparam name="TCommandResult">The type of command result</typeparam>
public interface ICommandHandler<in TCommand, TCommandResult> : ICommandHandlerBase,
                                                                IAsyncHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
{
}