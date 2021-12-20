using System.Reflection;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public class LiteBusMessageBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusMessageBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public LiteBusMessageBuilder RegisterFrom(Assembly assembly)
    {
        return this;
    }

    public LiteBusMessageBuilder RegisterHandler<THandler>() where THandler : IMessageHandler
    {
        _messageRegistry.Register(typeof(THandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterPreHandler<TPreHandler>() where TPreHandler : IMessagePreHandler
    {
        _messageRegistry.Register(typeof(TPreHandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterPostHandler<TPostHandler>() where TPostHandler : IMessagePostHandler
    {
        _messageRegistry.Register(typeof(TPostHandler));

        return this;
    }

    public LiteBusMessageBuilder RegisterErrorHandler<TErrorHandler>() where TErrorHandler : IMessageErrorHandler
    {
        _messageRegistry.Register(typeof(TErrorHandler));

        return this;
    }
}