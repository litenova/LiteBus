using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ICommandHandlerBase : ICommandConstruct
{
}

/// <summary>
///     Represents the definition of a handler that handles a command without result
/// </summary>
/// <typeparam name="TCommand">The type of command</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandlerBase, IAsyncMessageHandler<TCommand>
    where TCommand : ICommand
{
}

/// <summary>
///     Represents the definition of a handler that handles a command with result
/// </summary>
/// <typeparam name="TCommand">The type of command to</typeparam>
/// <typeparam name="TCommandResult">The type of command result</typeparam>
public interface ICommandHandler<in TCommand, TCommandResult> : ICommandHandlerBase,
                                                                IAsyncMessageHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
{
}