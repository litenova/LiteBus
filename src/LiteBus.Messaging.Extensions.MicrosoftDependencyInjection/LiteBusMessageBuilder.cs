using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public class LiteBusMessageBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusMessageBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public LiteBusMessageBuilder RegisterFrom<TMessage>()
    {
        _messageRegistry.Register(typeof(TMessage));

        return this;
    }

    public LiteBusMessageBuilder RegisterFrom(Assembly assembly)
    {
        _messageRegistry.RegisterFrom(assembly);

        return this;
    }

    public LiteBusMessageBuilder RegisterHandler<THandler>() where THandler : IHandler
    {
        _messageRegistry.Register(typeof(THandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterHandler(Type handlerType)
    {
        _messageRegistry.Register(handlerType);

        return this;
    }

    public LiteBusMessageBuilder RegisterPreHandler<TPreHandler>() where TPreHandler : IPreHandler
    {
        _messageRegistry.Register(typeof(TPreHandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterPreHandler(Type preHandlerType)
    {
        _messageRegistry.Register(preHandlerType);

        return this;
    }

    public LiteBusMessageBuilder RegisterPostHandler<TPostHandler>() where TPostHandler : IPostHandler
    {
        _messageRegistry.Register(typeof(TPostHandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterPostHandler(Type postHandlerType)
    {
        _messageRegistry.Register(postHandlerType);

        return this;
    }

    public LiteBusMessageBuilder RegisterErrorHandler<TErrorHandler>() where TErrorHandler : IErrorHandler
    {
        _messageRegistry.Register(typeof(TErrorHandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterErrorHandler(Type errorHandlerType)
    {
        _messageRegistry.Register(errorHandlerType);

        return this;
    }
}