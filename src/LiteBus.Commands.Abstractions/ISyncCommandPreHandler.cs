using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPreHandler : ICommandHandler, ISyncPreHandler<ICommand>
{
}