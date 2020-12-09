namespace Paykan.Abstractions.Interceptors
{
    public interface ICommandPostHandleHook : IPostHandleHook<IBaseCommand>
    {
    }

    public interface ICommandPostHandleHook<in TCommand> : IPostHandleHook<TCommand> where TCommand : IBaseCommand
    {
    }
}