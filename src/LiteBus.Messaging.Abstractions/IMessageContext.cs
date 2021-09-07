namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageContext
    {
        ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

        ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

        ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }
    }
}