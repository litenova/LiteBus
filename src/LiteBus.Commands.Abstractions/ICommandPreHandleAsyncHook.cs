using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each command pre-handle phase
    /// </summary>
    public interface ICommandPreHandleAsyncHook : IPreHandleAsyncHook<ICommandBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> pre-handle phase
    /// </summary>
    public interface ICommandPreHandleAsyncHook<in TCommand> : IPreHandleAsyncHook<TCommand>
        where TCommand : ICommandBase
    {
    }
}