using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Internal.Exceptions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator;

public class MessageContext : IMessageContext
{
    private readonly Type _messageType;
    private readonly IServiceProvider _serviceProvider;

    public MessageContext(Type messageType, IMessageDescriptor descriptor, IServiceProvider serviceProvider)
    {
        _messageType = messageType;
        _serviceProvider = serviceProvider;

        Handlers = ResolveHandlers(descriptor.Handlers).ToLazyReadOnlyCollection();
        IndirectHandlers = ResolveHandlers(descriptor.IndirectHandlers).ToLazyReadOnlyCollection();

        PostHandlers = ResolvePostHandlers(descriptor.PostHandlers).ToLazyReadOnlyCollection();
        IndirectPostHandlers = ResolvePostHandlers(descriptor.IndirectPostHandlers).ToLazyReadOnlyCollection();

        PreHandlers = ResolvePreHandlers(descriptor.PreHandlers).ToLazyReadOnlyCollection();
        IndirectPreHandlers = ResolvePreHandlers(descriptor.IndirectPreHandlers).ToLazyReadOnlyCollection();

        ErrorHandlers = ResolveErrorHandlers(descriptor.ErrorHandlers).ToLazyReadOnlyCollection();
        IndirectErrorHandlers = ResolveErrorHandlers(descriptor.IndirectErrorHandlers).ToLazyReadOnlyCollection();
    }

    public ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

    public ILazyReadOnlyCollection<IMessageHandler> IndirectHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePreHandler> IndirectPreHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePostHandler> IndirectPostHandlers { get; }

    public ILazyReadOnlyCollection<IMessageErrorHandler> ErrorHandlers { get; }

    public ILazyReadOnlyCollection<IMessageErrorHandler> IndirectErrorHandlers { get; }

    public ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

    private IEnumerable<Lazy<IMessageHandler>> ResolveHandlers(
        IReadOnlyCollection<IHandlerDescriptor> handlerDescriptors)
    {
        foreach (var handlerDescriptor in handlerDescriptors.OrderBy(h => h.Order))
        {
            var handlerType = handlerDescriptor.HandlerType;

            if (handlerDescriptor.IsGeneric)
            {
                handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(handlerType));

            yield return new Lazy<IMessageHandler>(() =>
            {
                var handler = resolveFunc();

                if (handler is null)
                {
                    throw new NotResolvedException(handlerType);
                }

                return (IMessageHandler) handler;
            });
        }
    }

    private IEnumerable<Lazy<IMessagePreHandler>> ResolvePreHandlers(
        IReadOnlyCollection<IPreHandlerDescriptor> descriptors)
    {
        foreach (var handlerDescriptor in descriptors.OrderBy(d => d.Order))
        {
            var preHandlerType = handlerDescriptor.PreHandlerType;

            if (handlerDescriptor.IsGeneric)
            {
                preHandlerType = preHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(preHandlerType));

            yield return new Lazy<IMessagePreHandler>(() =>
            {
                var preHandler = resolveFunc();

                if (preHandler is null)
                {
                    throw new NotResolvedException(preHandlerType);
                }

                return (IMessagePreHandler) preHandler;
            });
        }
    }

    private IEnumerable<Lazy<IMessageErrorHandler>> ResolveErrorHandlers(
        IReadOnlyCollection<IErrorHandlerDescriptor> descriptors)
    {
        foreach (var handlerDescriptor in descriptors.OrderBy(d => d.Order))
        {
            var errorHandlerType = handlerDescriptor.ErrorHandlerType;

            if (handlerDescriptor.IsGeneric)
            {
                errorHandlerType = errorHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(errorHandlerType));

            yield return new Lazy<IMessageErrorHandler>(() =>
            {
                var errorHandler = resolveFunc();

                if (errorHandler is null)
                {
                    throw new NotResolvedException(errorHandlerType);
                }

                return (IMessageErrorHandler) errorHandler;
            });
        }
    }

    private IEnumerable<Lazy<IMessagePostHandler>> ResolvePostHandlers(
        IReadOnlyCollection<IPostHandlerDescriptor> descriptors)
    {
        foreach (var handlerDescriptor in descriptors.OrderBy(d => d.Order))
        {
            var postHandlerType = handlerDescriptor.PostHandlerType;

            if (handlerDescriptor.IsGeneric)
            {
                postHandlerType = postHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(postHandlerType));

            yield return new Lazy<IMessagePostHandler>(() =>
            {
                var postHandler = resolveFunc();

                if (postHandler is null)
                {
                    throw new NotResolvedException(postHandlerType);
                }

                return (IMessagePostHandler) postHandler;
            });
        }
    }
}