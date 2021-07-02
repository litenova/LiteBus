using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each command pre-handle phase
    /// </summary>
    public interface ICommandPreHandleHook : IPreHandleHook
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> pre-handle phase
    /// </summary>
    public interface ICommandPreHandleHook<in TCommand> : IPreHandleHook<TCommand> where TCommand : IMessage
    {
    }
}