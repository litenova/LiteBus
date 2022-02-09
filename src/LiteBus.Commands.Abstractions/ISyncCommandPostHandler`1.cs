using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

public interface ISyncCommandPostHandler<in TCommand> : ICommandPostHandlerBase, IAsyncPostHandler<TCommand>
    where TCommand : ICommandBase
{
}