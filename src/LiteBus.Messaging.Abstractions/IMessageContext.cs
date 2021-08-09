namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageContext
    {
        ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

        ILazyReadOnlyCollection<IAsyncHook> PostHandleAsyncHooks { get; }

        ILazyReadOnlyCollection<IAsyncHook> PreHandleAsyncHooks { get; }
    }
}