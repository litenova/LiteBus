using System;
using System.Linq;
using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

public class LiteBusCommandBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusCommandBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    /// <summary>
    ///     Register a command or command handler (i.e., handlers, pre-handlers, post-handlers, error-handlers)
    /// </summary>
    /// <typeparam name="T">The type of command or command handler</typeparam>
    /// <returns>The instance of <see cref="LiteBusCommandBuilder"/></returns>
    public LiteBusCommandBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    public LiteBusCommandBuilder Register(Type type)
    {
        if (type.IsAssignableTo(typeof(ICommandHandler)) || type.IsAssignableTo(typeof(ICommand)))
        {
            _messageRegistry.Register(type);
            return this;
        }

        throw new NotSupportedException($"The type of '{type.Name}' cannot be registered");
    }

    public LiteBusCommandBuilder RegisterFrom(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAssignableTo(typeof(ICommandHandler)) || type.IsAssignableTo(typeof(ICommand)))
            {
                _messageRegistry.Register(type);
            }
        }

        return this;
    }
}