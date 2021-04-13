using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents the definition of a pre handler hook that executes on each command pre-handle phase
    /// </summary>
    public interface ICommandPreHandleHook : IPreHandleHook<IBaseCommand>
    {
        
    }

    /// <summary>
    ///     Represents the definition of a pre handler hook that executes on the <see cref="TCommand" /> pre-handle phase
    /// </summary>
    public interface ICommandPreHandleHook<in TCommand> : IPreHandleHook<TCommand> where TCommand : IBaseCommand
    {
    }
}