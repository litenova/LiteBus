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

    public IInstances<IHandlerDescriptor> Handlers { get; }

    public IInstances<IHandlerDescriptor> IndirectHandlers { get; }

    public IInstances<IPreHandlerDescriptor> PreHandlers { get; }

    public IInstances<IPreHandlerDescriptor> IndirectPreHandlers { get; }

    public IInstances<IPostHandlerDescriptor> PostHandlers { get; }

    public IInstances<IPostHandlerDescriptor> IndirectPostHandlers { get; }

    public IInstances<IErrorHandlerDescriptor> ErrorHandlers { get; }

    public IInstances<IErrorHandlerDescriptor> IndirectErrorHandlers { get; }

    private IEnumerable<LazyInstance<IHandlerDescriptor>> ResolveHandlers(IEnumerable<IHandlerDescriptor> descriptors)
    {
        foreach (var handlerDescriptor in descriptors.OrderBy(h => h.Order))
        {
            var handlerType = handlerDescriptor.HandlerType;

            if (handlerDescriptor.IsGeneric)
            {
                handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(handlerType));

            var lazy = new Lazy<IHandler>(() =>
            {
                var handler = resolveFunc();

                if (handler is null)
                {
                    throw new NotResolvedException(handlerType);
                }

                return (IHandler) handler;
            });

            yield return new LazyInstance<IHandlerDescriptor>(lazy, handlerDescriptor);
        }
    }

    private IEnumerable<LazyInstance<IPreHandlerDescriptor>> ResolvePreHandlers(
        IEnumerable<IPreHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors.OrderBy(d => d.Order))
        {
            var preHandlerType = descriptor.HandlerType;

            if (descriptor.IsGeneric)
            {
                preHandlerType = preHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(preHandlerType));

            var lazy = new Lazy<IHandler>(() =>
            {
                var preHandler = resolveFunc();

                if (preHandler is null)
                {
                    throw new NotResolvedException(preHandlerType);
                }

                return (IHandler) preHandler;
            });

            yield return new LazyInstance<IPreHandlerDescriptor>(lazy, descriptor);
        }
    }

    private IEnumerable<LazyInstance<IErrorHandlerDescriptor>> ResolveErrorHandlers(
        IEnumerable<IErrorHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors.OrderBy(d => d.Order))
        {
            var errorHandlerType = descriptor.HandlerType;

            if (descriptor.IsGeneric)
            {
                errorHandlerType = errorHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(errorHandlerType));

            var lazy = new Lazy<IHandler>(() =>
            {
                var errorHandler = resolveFunc();

                if (errorHandler is null)
                {
                    throw new NotResolvedException(errorHandlerType);
                }

                return (IHandler) errorHandler;
            });

            yield return new LazyInstance<IErrorHandlerDescriptor>(lazy, descriptor);
        }
    }

    private IEnumerable<LazyInstance<IPostHandlerDescriptor>> ResolvePostHandlers(
        IEnumerable<IPostHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors.OrderBy(d => d.Order))
        {
            var postHandlerType = descriptor.HandlerType;

            if (descriptor.IsGeneric)
            {
                postHandlerType = postHandlerType.MakeGenericType(_messageType.GetGenericArguments());
            }

            var resolveFunc = new Func<object>(() => _serviceProvider.GetService(postHandlerType));

            var lazy = new Lazy<IHandler>(() =>
            {
                var postHandler = resolveFunc();

                if (postHandler is null)
                {
                    throw new NotResolvedException(postHandlerType);
                }

                return (IHandler) postHandler;
            });

            yield return new LazyInstance<IPostHandlerDescriptor>(lazy, descriptor);
        }
    }
}