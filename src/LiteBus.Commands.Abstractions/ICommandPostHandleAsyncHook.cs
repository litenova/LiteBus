using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each command post-handle phase
    /// </summary>
    public interface ICommandPostHandleAsyncHook : IPostHandleAsyncHook<ICommandBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> post-handle phase
    /// </summary>
    public interface ICommandPostHandleAsyncHook<in TCommand> : IPostHandleAsyncHook<TCommand>
        where TCommand : ICommandBase
    {
    }
}