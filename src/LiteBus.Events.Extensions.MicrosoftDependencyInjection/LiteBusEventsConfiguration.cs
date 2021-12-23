using System;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

public class LiteBusEventsConfiguration
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusEventsConfiguration(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public LiteBusEventsConfiguration RegisterFrom(Assembly assembly)
    {
        _messageRegistry.RegisterFrom<IEventConstruct>(assembly);

        return this;
    }

    public LiteBusEventsConfiguration RegisterHandler<THandler>() where THandler : IEventHandlerBase
    {
        _messageRegistry.Register(typeof(THandler));

        return this;
    }

    public LiteBusEventsConfiguration RegisterHandler(Type handlerType)
    {
        _messageRegistry.Register(handlerType);

        return this;
    }

    public LiteBusEventsConfiguration RegisterPreHandler<TEventPreHandler>()
        where TEventPreHandler : IEventPreHandlerBase
    {
        _messageRegistry.Register(typeof(TEventPreHandler));

        return this;
    }

    public LiteBusEventsConfiguration RegisterPreHandler(Type eventPreHandlerType)
    {
        _messageRegistry.Register(eventPreHandlerType);

        return this;
    }

    public LiteBusEventsConfiguration RegisterPostHandler<TEventPostHandler>()
        where TEventPostHandler : IEventPostHandlerBase
    {
        _messageRegistry.Register(typeof(TEventPostHandler));

        return this;
    }

    public LiteBusEventsConfiguration RegisterPostHandler(Type eventPostHandlerType)
    {
        _messageRegistry.Register(eventPostHandlerType);

        return this;
    }

    public LiteBusEventsConfiguration RegisterErrorHandler<TEventErrorHandler>()
        where TEventErrorHandler : IEventErrorHandlerBase
    {
        _messageRegistry.Register(typeof(TEventErrorHandler));

        return this;
    }

    public LiteBusEventsConfiguration RegisterErrorHandler(Type eventErrorHandlerType)
    {
        _messageRegistry.Register(eventErrorHandlerType);

        return this;
    }
}