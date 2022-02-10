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

    public LiteBusMessageBuilder RegisterFrom(Assembly assembly)
    {
        _messageRegistry.RegisterFrom(assembly);

        return this;
    }

    public LiteBusMessageBuilder Register<T>() where T : IHandler
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    public LiteBusMessageBuilder Register(Type type)
    {
        _messageRegistry.Register(type);

        return this;
    }
}