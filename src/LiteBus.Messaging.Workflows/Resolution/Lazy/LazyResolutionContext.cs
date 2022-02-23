using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Workflows.Resolution.Lazy;

public class LazyResolutionContext : IResolutionContext
{
    public LazyResolutionContext(IInstances<IHandlerDescriptor> handlers,
                                 IInstances<IHandlerDescriptor> indirectHandlers,
                                 IInstances<IPreHandlerDescriptor> preHandlers,
                                 IInstances<IPreHandlerDescriptor> indirectPreHandlers,
                                 IInstances<IPostHandlerDescriptor> postHandlers,
                                 IInstances<IPostHandlerDescriptor> indirectPostHandlers,
                                 IInstances<IErrorHandlerDescriptor> errorHandlers,
                                 IInstances<IErrorHandlerDescriptor> indirectErrorHandlers)
    {
        Handlers = handlers;
        IndirectHandlers = indirectHandlers;
        PreHandlers = preHandlers;
        IndirectPreHandlers = indirectPreHandlers;
        PostHandlers = postHandlers;
        IndirectPostHandlers = indirectPostHandlers;
        ErrorHandlers = errorHandlers;
        IndirectErrorHandlers = indirectErrorHandlers;
    }

    public IInstances<IHandlerDescriptor> Handlers { get; }

    public IInstances<IHandlerDescriptor> IndirectHandlers { get; }

    public IInstances<IPreHandlerDescriptor> PreHandlers { get; }

    public IInstances<IPreHandlerDescriptor> IndirectPreHandlers { get; }

    public IInstances<IPostHandlerDescriptor> PostHandlers { get; }

    public IInstances<IPostHandlerDescriptor> IndirectPostHandlers { get; }

    public IInstances<IErrorHandlerDescriptor> ErrorHandlers { get; }

    public IInstances<IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}