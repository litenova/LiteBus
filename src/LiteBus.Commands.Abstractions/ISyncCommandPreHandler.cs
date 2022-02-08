using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPreHandler : ICommandPreHandlerBase, ISyncPreHandler<ICommandBase>
{
}