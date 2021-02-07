using Paykan.Messaging.Abstractions;

namespace Paykan.Commands.Abstraction
{
    /// <summary>
    ///     Represents the definition of a post handler hook that executes on each command post-handle phase
    /// </summary>
    public interface ICommandPostHandleHook : IPostHandleHook<IBaseCommand>
    {
    }

    /// <summary>
    ///     Represents the definition of a post handler hook that executes on the <see cref="TCommand" /> post-handle phase
    /// </summary>
    public interface ICommandPostHandleHook<in TCommand> : IPostHandleHook<TCommand> where TCommand : IBaseCommand
    {
    }
}