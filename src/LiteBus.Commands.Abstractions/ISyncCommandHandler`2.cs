using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the definition of a sync handler that handles a command result
/// </summary>
/// <typeparam name="TCommand">The type of command</typeparam>
/// <typeparam name="TCommandResult">The type of command result</typeparam>
public interface ISyncCommandHandler<in TCommand, out TCommandResult> : ICommandHandler,
                                                                        ISyncHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
{
}