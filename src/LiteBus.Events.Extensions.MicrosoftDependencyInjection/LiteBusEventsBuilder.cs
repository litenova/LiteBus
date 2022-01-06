using System;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

public class LiteBusEventsBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusEventsBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public LiteBusEventsBuilder Register<TEvent>() where TEvent : IEvent
    {
        _messageRegistry.Register(typeof(TEvent));
        return this;
    }
    
    public LiteBusEventsBuilder RegisterFrom(Assembly assembly)
    {
        _messageRegistry.RegisterFrom<IEventConstruct>(assembly);

        return this;
    }

    public LiteBusEventsBuilder RegisterHandler<THandler>() where THandler : IEventHandlerBase
    {
        _messageRegistry.Register(typeof(THandler));

        return this;
    }

    public LiteBusEventsBuilder RegisterHandler(Type handlerType)
    {
        _messageRegistry.Register(handlerType);

        return this;
    }

    public LiteBusEventsBuilder RegisterPreHandler<TEventPreHandler>()
        where TEventPreHandler : IEventPreHandlerBase
    {
        _messageRegistry.Register(typeof(TEventPreHandler));

        return this;
    }

    public LiteBusEventsBuilder RegisterPreHandler(Type eventPreHandlerType)
    {
        _messageRegistry.Register(eventPreHandlerType);

        return this;
    }

    public LiteBusEventsBuilder RegisterPostHandler<TEventPostHandler>()
        where TEventPostHandler : IEventPostHandlerBase
    {
        _messageRegistry.Register(typeof(TEventPostHandler));

        return this;
    }

    public LiteBusEventsBuilder RegisterPostHandler(Type eventPostHandlerType)
    {
        _messageRegistry.Register(eventPostHandlerType);

        return this;
    }

    public LiteBusEventsBuilder RegisterErrorHandler<TEventErrorHandler>()
        where TEventErrorHandler : IEventErrorHandlerBase
    {
        _messageRegistry.Register(typeof(TEventErrorHandler));

        return this;
    }

    public LiteBusEventsBuilder RegisterErrorHandler(Type eventErrorHandlerType)
    {
        _messageRegistry.Register(eventErrorHandlerType);

        return this;
    }
}