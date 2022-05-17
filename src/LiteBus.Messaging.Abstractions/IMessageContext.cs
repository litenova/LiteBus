namespace LiteBus.Messaging.Abstractions;

public interface IMessageContext
{
    ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

    ILazyReadOnlyCollection<IMessageHandler> IndirectHandlers { get; }

    ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

    ILazyReadOnlyCollection<IMessagePreHandler> IndirectPreHandlers { get; }

    ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }

    ILazyReadOnlyCollection<IMessagePostHandler> IndirectPostHandlers { get; }

    ILazyReadOnlyCollection<IMessageErrorHandler> ErrorHandlers { get; }

    ILazyReadOnlyCollection<IMessageErrorHandler> IndirectErrorHandlers { get; }
}