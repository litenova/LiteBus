using System;
using System.Linq;
using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

public class CommandModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public CommandModuleBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public CommandModuleBuilder Register<T>() where T : IRegistrableCommandConstruct
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    public CommandModuleBuilder Register(Type type)
    {
        if (!type.IsAssignableTo(typeof(IRegistrableCommandConstruct)))
        {
            throw new NotSupportedException($"The given type '{type.Name}' is not a command construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    public CommandModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        foreach (var registrableCommandConstruct in assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IRegistrableCommandConstruct))))
        {
            _messageRegistry.Register(registrableCommandConstruct);
        }

        return this;
    }
}