using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each command post-handle phase
    /// </summary>
    public interface ICommandPostHandleHook : IPostHandleHook<ICommandBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> post-handle phase
    /// </summary>
    public interface ICommandPostHandleHook<in TCommand> : IPostHandleHook<TCommand> where TCommand : ICommandBase
    {
    }
}