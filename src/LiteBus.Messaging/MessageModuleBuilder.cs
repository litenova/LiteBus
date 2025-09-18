using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

public sealed class MessageModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public MessageModuleBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public MessageModuleBuilder Register<T>()
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    public MessageModuleBuilder Register(Type type)
    {
        _messageRegistry.Register(type);
        return this;
    }

    public MessageModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            _messageRegistry.Register(type);
        }

        return this;
    }
}