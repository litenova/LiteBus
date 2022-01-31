using System.Collections.Generic;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageContext
{
    IInstances<IMessageHandler, IHandlerDescriptor> Handlers { get; }

    IInstances<IMessageHandler, IHandlerDescriptor> IndirectHandlers { get; }

    IInstances<IMessagePreHandler, IPreHandlerDescriptor> PreHandlers { get; }

    IInstances<IMessagePreHandler, IPreHandlerDescriptor> IndirectPreHandlers { get; }

    IInstances<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    IInstances<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    IInstances<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    IInstances<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}