using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Internal.Exceptions;
using LiteBus.Messaging.Internal.Resolution;

namespace LiteBus.Messaging.Internal.Mediator;

public class MessageContext : IMessageContext
{
    private readonly Type _messageType;
    private readonly IServiceProvider _serviceProvider;

    public MessageContext(Type messageType, IMessageDescriptor descriptor, IServiceProvider serviceProvider)
    {
        _messageType = messageType;
        _serviceProvider = serviceProvider;

        Handlers = ResolveHandlers(descriptor.Handlers).ToInstances();
        IndirectHandlers = ResolveHandlers(descriptor.IndirectHandlers).ToInstances();

        PostHandlers = ResolvePostHandlers(descriptor.PostHandlers).ToInstances();
        IndirectPostHandlers = ResolvePostHandlers(descriptor.IndirectPostHandlers).ToInstances();

        PreHandlers = ResolvePreHandlers(descriptor.PreHandlers).ToInstances();
        IndirectPreHandlers = ResolvePreHandlers(descriptor.IndirectPreHandlers).ToInstances();

        ErrorHandlers = ResolveErrorHandlers(descriptor.ErrorHandlers).ToInstances();
        IndirectErrorHandlers = ResolveErrorHandlers(descriptor.IndirectErrorHandlers).ToInstances();
    }

    private IEnumerable<LazyInstance<IMessageHandler, IHandlerDescriptor>> ResolveHandlers(
        IEnumerable<IHandlerDescriptor> descriptors)
    {
        foreach (var handlerDescriptor in descriptors.OrderBy(h => h.Order))
        {
            var handlerType = handlerDescriptor.HandlerType;

            if (handlerDescriptor.IsGeneric)
            {
                handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(handlerType));

            var lazy = new Lazy<IMessageHandler>(() =>
            {
                var handler = resolveFunc();

                if (handler is null)
                {
                    throw new NotResolvedException(handlerType);
                }

                return (IMessageHandler) handler;
            });

            yield return new LazyInstance<IMessageHandler, IHandlerDescriptor>(lazy, handlerDescriptor);
        }
    }

    private IEnumerable<LazyInstance<IMessagePreHandler, IPreHandlerDescriptor>> ResolvePreHandlers(
        IEnumerable<IPreHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors.OrderBy(d => d.Order))
        {
            var preHandlerType = descriptor.PreHandlerType;

            if (descriptor.IsGeneric)
            {
                preHandlerType = preHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(preHandlerType));

            var lazy = new Lazy<IMessagePreHandler>(() =>
            {
                var preHandler = resolveFunc();

                if (preHandler is null)
                {
                    throw new NotResolvedException(preHandlerType);
                }

                return (IMessagePreHandler) preHandler;
            });

            yield return new LazyInstance<IMessagePreHandler, IPreHandlerDescriptor>(lazy, descriptor);
        }
    }

    private IEnumerable<LazyInstance<IMessageErrorHandler, IErrorHandlerDescriptor>> ResolveErrorHandlers(
        IEnumerable<IErrorHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors.OrderBy(d => d.Order))
        {
            var errorHandlerType = descriptor.ErrorHandlerType;

            if (descriptor.IsGeneric)
            {
                errorHandlerType = errorHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(errorHandlerType));

            var lazy = new Lazy<IMessageErrorHandler>(() =>
            {
                var errorHandler = resolveFunc();

                if (errorHandler is null)
                {
                    throw new NotResolvedException(errorHandlerType);
                }

                return (IMessageErrorHandler) errorHandler;
            });

            yield return new LazyInstance<IMessageErrorHandler, IErrorHandlerDescriptor>(lazy, descriptor);
        }
    }

    private IEnumerable<LazyInstance<IMessagePostHandler, IPostHandlerDescriptor>> ResolvePostHandlers(
        IEnumerable<IPostHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors.OrderBy(d => d.Order))
        {
            var postHandlerType = descriptor.PostHandlerType;

            if (descriptor.IsGeneric)
            {
                postHandlerType = postHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(postHandlerType));

            var lazy = new Lazy<IMessagePostHandler>(() =>
            {
                var postHandler = resolveFunc();

                if (postHandler is null)
                {
                    throw new NotResolvedException(postHandlerType);
                }

                return (IMessagePostHandler) postHandler;
            });

            yield return new LazyInstance<IMessagePostHandler, IPostHandlerDescriptor>(lazy, descriptor);
        }
    }

    public IInstances<IMessageHandler, IHandlerDescriptor> Handlers { get; }

    public IInstances<IMessageHandler, IHandlerDescriptor> IndirectHandlers { get; }

    public IInstances<IMessagePreHandler, IPreHandlerDescriptor> PreHandlers { get; }

    public IInstances<IMessagePreHandler, IPreHandlerDescriptor> IndirectPreHandlers { get; }

    public IInstances<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    public IInstances<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    public IInstances<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    public IInstances<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}