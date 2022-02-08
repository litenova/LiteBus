using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageContext
{
    IInstances<IHandler, IHandlerDescriptor> Handlers { get; }

    IInstances<IHandler, IHandlerDescriptor> IndirectHandlers { get; }

    IInstances<IPreHandler, IPreHandlerDescriptor> PreHandlers { get; }

    IInstances<IPreHandler, IPreHandlerDescriptor> IndirectPreHandlers { get; }

    IInstances<IPostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    IInstances<IPostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    IInstances<IErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    IInstances<IErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}