using System;
using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

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
    /// <returns>The instance of <see cref="LiteBusCommandBuilder" /></returns>
    /// <exception cref="NotSupportedException">In case the given type is neither command nor command handler</exception>
    public LiteBusCommandBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Register a command or command handler (i.e., handlers, pre-handlers, post-handlers, error-handlers)
    /// </summary>
    /// <param name="type">The type of command or command handler</param>
    /// <returns>The instance of <see cref="LiteBusCommandBuilder" /></returns>
    /// <exception cref="NotSupportedException">In case the given type is neither command nor command handler</exception>
    public LiteBusCommandBuilder Register(Type type)
    {
        if (type.IsAssignableTo(typeof(ICommandHandler)) || type.IsAssignableTo(typeof(ICommand)))
        {
            _messageRegistry.Register(type);
            return this;
        }

        throw new NotSupportedException($"The type of '{type.Name}' cannot be registered");
    }

    /// <summary>
    ///     Registers commands and command handlers found in the given assembly
    /// </summary>
    /// <param name="assembly">The assembly to search</param>
    /// <returns>The instance of <see cref="LiteBusCommandBuilder" /></returns>
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