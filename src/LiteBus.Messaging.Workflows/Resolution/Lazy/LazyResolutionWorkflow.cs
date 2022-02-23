using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Resolution.Exceptions;

namespace LiteBus.Messaging.Workflows.Resolution.Lazy;

public class LazyResolutionWorkflow : IResolutionWorkflow
{
    private Type _messageType;
    private IServiceProvider _serviceProvider;

    public IResolutionContext Resolve(IMessageDescriptor messageDescriptor, IServiceProvider serviceProvider)
    {
        _messageType = messageDescriptor.MessageType;
        _serviceProvider = serviceProvider;

        return new LazyResolutionContext(ResolveLazies(messageDescriptor.Handlers),
                                         ResolveLazies(messageDescriptor.IndirectHandlers),
                                         ResolveLazies(messageDescriptor.PreHandlers),
                                         ResolveLazies(messageDescriptor.IndirectPreHandlers),
                                         ResolveLazies(messageDescriptor.PostHandlers),
                                         ResolveLazies(messageDescriptor.IndirectPostHandlers),
                                         ResolveLazies(messageDescriptor.ErrorHandlers),
                                         ResolveLazies(messageDescriptor.IndirectErrorHandlers));
    }

    private LazyInstances<TDescriptor> ResolveLazies<TDescriptor>(IEnumerable<TDescriptor> descriptors)
        where TDescriptor : IHandlerDescriptor
    {
        return new LazyInstances<TDescriptor>(descriptors.OrderBy(d => d.Order).Select(ResolveLazy));
    }

    private LazyInstance<TDescriptor> ResolveLazy<TDescriptor>(TDescriptor descriptor)
        where TDescriptor : IHandlerDescriptor
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

        return new LazyInstance<TDescriptor>(lazy, descriptor);
    }
}