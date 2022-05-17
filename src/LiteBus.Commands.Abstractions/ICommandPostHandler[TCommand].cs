using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TCommand" /> post-handle phase
/// </summary>
public interface ICommandPostHandler<in TCommand> : ICommandPostHandlerBase, IMessagePostHandler<TCommand>
    where TCommand : ICommandBase
{
}