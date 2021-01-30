using Paykan.Messaging.Abstractions;

namespace Paykan.Commands.Abstraction
{
    public interface ICommandPostHandleHook : IPostHandleHook<IBaseCommand>
    {
    }

    public interface ICommandPostHandleHook<in TCommand> : IPostHandleHook<TCommand> where TCommand : IBaseCommand
    {
    }
}