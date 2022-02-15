using System;
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
        foreach (var type in assembly.GetTypes())
        {
            _messageRegistry.Register(type);
        }

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