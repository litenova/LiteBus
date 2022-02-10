using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageContext
{
    IInstances<IHandlerDescriptor> Handlers { get; }

    IInstances<IHandlerDescriptor> IndirectHandlers { get; }

    IInstances<IPreHandlerDescriptor> PreHandlers { get; }

    IInstances<IPreHandlerDescriptor> IndirectPreHandlers { get; }

    IInstances<IPostHandlerDescriptor> PostHandlers { get; }

    IInstances<IPostHandlerDescriptor> IndirectPostHandlers { get; }

    IInstances<IErrorHandlerDescriptor> ErrorHandlers { get; }

    IInstances<IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}