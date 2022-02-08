using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPostHandler<in TCommand, in TCommandResult> : ICommandPostHandlerBase,
                                                                           ISyncPostHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
{
}