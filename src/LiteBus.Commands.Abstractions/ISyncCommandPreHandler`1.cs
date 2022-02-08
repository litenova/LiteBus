using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPreHandler<in TCommand> : ICommandPreHandlerBase, ISyncPreHandler<TCommand>
    where TCommand : ICommandBase
{
}

