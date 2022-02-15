using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPostHandler<in TCommand> : ICommandHandler, ISyncPostHandler<TCommand> where TCommand : ICommand
{
}