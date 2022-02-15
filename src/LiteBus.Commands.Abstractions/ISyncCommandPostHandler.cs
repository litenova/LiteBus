using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPostHandler : ICommandHandler, ISyncPostHandler<ICommand>
{
}