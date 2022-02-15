using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPreHandler<in TCommand> : ICommandHandler, ISyncPreHandler<TCommand>
    where TCommand : ICommand
{
}