using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPostHandler<in TCommand, in TCommandResult> : ICommandHandler,
                                                                           ISyncPostHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
{
}