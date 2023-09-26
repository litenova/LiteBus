using System;
using System.Linq;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

public class EventModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public EventModuleBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public EventModuleBuilder Register<T>() where T : IRegistrableEventConstruct
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    public EventModuleBuilder Register(Type type)
    {
        if (!type.IsAssignableTo(typeof(IRegistrableEventConstruct)))
        {
            throw new NotSupportedException($"The given type '{type.Name}' is not a event construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    public EventModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        foreach (var registrableEventConstruct in assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IRegistrableEventConstruct))))
        {
            _messageRegistry.Register(registrableEventConstruct);
        }

        return this;
    }
}