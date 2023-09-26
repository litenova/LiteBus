using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator;

/// <inheritdoc cref="IMessageDependencies"/>
public class MessageDependencies : IMessageDependencies
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

    public ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

    public ILazyReadOnlyCollection<IMessageHandler> IndirectHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePreHandler> IndirectPreHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePostHandler> IndirectPostHandlers { get; }

    public ILazyReadOnlyCollection<IMessageErrorHandler> ErrorHandlers { get; }

    public ILazyReadOnlyCollection<IMessageErrorHandler> IndirectErrorHandlers { get; }

    /// <summary>
    /// Resolves handlers from the provided descriptors and a handler resolution function.
    /// </summary>
    private ILazyReadOnlyCollection<THandler> ResolveHandlers<TDescriptor, THandler>(
        IReadOnlyCollection<TDescriptor> descriptors,
        Func<Type, THandler> resolveFunc)
    {
        return descriptors
            .OrderBy(d => GetOrder(d))
            .Select(d => new Lazy<THandler>(() => resolveFunc(GetHandlerType(d))))
            .ToLazyReadOnlyCollection();
    }

    /// <summary>
    /// Retrieves the order value from a descriptor.
    /// </summary>
    private static int GetOrder(object descriptor)
    {
        return (int) descriptor.GetType().GetProperty("Order").GetValue(descriptor);
    }

    /// <summary>
    /// Retrieves the handler type from a descriptor, adjusting for generic types as necessary.
    /// </summary>
    private Type GetHandlerType(object descriptor)
    {
        var handlerTypeProp = descriptor.GetType().GetProperty("HandlerType") ??
                              descriptor.GetType().GetProperty($"{descriptor.GetType().Name.Replace("Descriptor", string.Empty)}Type");

        var isGenericProp = descriptor.GetType().GetProperty("IsGeneric");

        var handlerType = (Type) handlerTypeProp.GetValue(descriptor);

        if ((bool) isGenericProp.GetValue(descriptor))
        {
            handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
        }

        return handlerType;
    }
}