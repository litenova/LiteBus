using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator;

/// <inheritdoc cref="IMessageDependencies"/>
internal sealed class MessageDependencies : IMessageDependencies
{
    private readonly Type _messageType;

    public MessageDependencies(Type messageType, IMessageDescriptor descriptor, IServiceProvider serviceProvider)
    {
        _messageType = messageType;

        Handlers = ResolveHandlers(descriptor.Handlers, (handlerType) => (IMessageHandler) serviceProvider.GetService(handlerType));
        IndirectHandlers = ResolveHandlers(descriptor.IndirectHandlers, (handlerType) => (IMessageHandler) serviceProvider.GetService(handlerType));

        PreHandlers = ResolveHandlers(descriptor.PreHandlers, (handlerType) => (IMessagePreHandler) serviceProvider.GetService(handlerType));
        IndirectPreHandlers = ResolveHandlers(descriptor.IndirectPreHandlers, (handlerType) => (IMessagePreHandler) serviceProvider.GetService(handlerType));

        PostHandlers = ResolveHandlers(descriptor.PostHandlers, (handlerType) => (IMessagePostHandler) serviceProvider.GetService(handlerType));
        IndirectPostHandlers = ResolveHandlers(descriptor.IndirectPostHandlers, (handlerType) => (IMessagePostHandler) serviceProvider.GetService(handlerType));

        ErrorHandlers = ResolveHandlers(descriptor.ErrorHandlers, (handlerType) => (IMessageErrorHandler) serviceProvider.GetService(handlerType));
        IndirectErrorHandlers = ResolveHandlers(descriptor.IndirectErrorHandlers, (handlerType) => (IMessageErrorHandler) serviceProvider.GetService(handlerType));
    }

    public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> Handlers { get; }

    public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> IndirectHandlers { get; }

    public ILazyHandlerCollection<IMessagePreHandler, IPreHandlerDescriptor> PreHandlers { get; }

    public ILazyHandlerCollection<IMessagePreHandler, IPreHandlerDescriptor> IndirectPreHandlers { get; }

    public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }

    /// <summary>
    /// Resolves handlers from the provided descriptors and a handler resolution function.
    /// </summary>
    private ILazyHandlerCollection<THandler, TDescriptor> ResolveHandlers<THandler, TDescriptor>(
        IEnumerable<TDescriptor> descriptors,
        Func<Type, THandler> resolveFunc) where TDescriptor : IHandlerDescriptor
    {
        return descriptors
            .OrderBy(d => d.Order)
            .Select(d => new LazyHandler<THandler, TDescriptor>
            {
                Handler = new Lazy<THandler>(() => resolveFunc(GetHandlerType(d))),
                Descriptor = d
            })
            .ToLazyReadOnlyCollection();
    }

    /// <summary>
    /// Retrieves the handler type from a descriptor, adjusting for generic types as necessary.
    /// </summary>
    private Type GetHandlerType(IHandlerDescriptor descriptor)
    {
        var handlerType = descriptor.HandlerType;

        if (descriptor.MessageType.IsGenericType)
        {
            handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
        }

        return handlerType;
    }
}