using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    public interface ICommandPreHandlerBase : ICommandConstruct
    {
    }

    /// <summary>
    ///     Represents an action that is executed on each command pre-handle phase
    /// </summary>
    public interface ICommandPreHandler : ICommandPreHandlerBase, IMessagePreHandler<ICommandBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> pre-handle phase
    /// </summary>
    public interface ICommandPreHandler<in TCommand> : ICommandPreHandlerBase, IMessagePreHandler<TCommand>
        where TCommand : ICommandBase
    {
    }
}