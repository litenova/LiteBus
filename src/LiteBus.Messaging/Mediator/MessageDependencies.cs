using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;

namespace LiteBus.Messaging.Mediator;

/// <inheritdoc cref="IMessageDependencies" />
internal sealed class MessageDependencies : IMessageDependencies
{
    private readonly Type _messageType;
    private readonly IEnumerable<string> _tags;

    public MessageDependencies(Type messageType,
                               IMessageDescriptor descriptor,
                               IServiceProvider serviceProvider,
                               IEnumerable<string> tags)
    {
        _messageType = messageType;
        _tags = tags;

        Handlers = ResolveHandlers(descriptor.Handlers, handlerType => (IMessageHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectHandlers = ResolveHandlers(descriptor.IndirectHandlers, handlerType => (IMessageHandler) serviceProvider.GetRequiredService(handlerType));

        PreHandlers = ResolveHandlers(descriptor.PreHandlers, handlerType => (IMessagePreHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectPreHandlers = ResolveHandlers(descriptor.IndirectPreHandlers, handlerType => (IMessagePreHandler) serviceProvider.GetRequiredService(handlerType));

        PostHandlers = ResolveHandlers(descriptor.PostHandlers, handlerType => (IMessagePostHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectPostHandlers = ResolveHandlers(descriptor.IndirectPostHandlers, handlerType => (IMessagePostHandler) serviceProvider.GetRequiredService(handlerType));

        ErrorHandlers = ResolveHandlers(descriptor.ErrorHandlers, handlerType => (IMessageErrorHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectErrorHandlers = ResolveHandlers(descriptor.IndirectErrorHandlers, handlerType => (IMessageErrorHandler) serviceProvider.GetRequiredService(handlerType));
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
    ///     Resolves handlers from the provided descriptors and a handler resolution function.
    /// </summary>
    private ILazyHandlerCollection<THandler, TDescriptor> ResolveHandlers<THandler, TDescriptor>(
        IEnumerable<TDescriptor> descriptors,
        Func<Type, THandler> resolveFunc) where TDescriptor : IHandlerDescriptor
    {
        return descriptors
            .OrderBy(d => d.Order)
            .Where(d => d.Tags.Count == 0 || d.Tags.Intersect(_tags).Any())
            .Select(d => new LazyHandler<THandler, TDescriptor>
            {
                Handler = new Lazy<THandler>(() => resolveFunc(GetHandlerType(d))),
                Descriptor = d
            })
            .ToLazyReadOnlyCollection();
    }

    /// <summary>
    ///     Retrieves the handler type from a descriptor, adjusting for generic types as necessary.
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