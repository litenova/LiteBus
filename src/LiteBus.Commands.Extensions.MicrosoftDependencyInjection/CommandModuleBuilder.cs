using System;
using System.Linq;
using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Builder class for registering command types in the message registry.
/// </summary>
public sealed class CommandModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry to which commands will be registered.</param>
    public CommandModuleBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    /// <summary>
    ///     Registers a command type for the message registry.
    /// </summary>
    /// <typeparam name="T">The type of command to register, which must implement <see cref="IRegistrableCommandConstruct" />.</typeparam>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    public CommandModuleBuilder Register<T>() where T : IRegistrableCommandConstruct
    {
        _messageRegistry.Register(typeof(T));
        return this;
    }

    /// <summary>
    ///     Registers a command type for the message registry.
    /// </summary>
    /// <param name="type">The type of command to register, which must implement <see cref="IRegistrableCommandConstruct" />.</param>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    public CommandModuleBuilder Register(Type type)
    {
        if (!type.IsAssignableTo(typeof(IRegistrableCommandConstruct)))
        {
            throw new NotSupportedException($"The given type '{type.Name}' is not a command construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all command types from the specified assembly that implement <see cref="IRegistrableCommandConstruct" />.
    /// </summary>
    /// <param name="assembly">The assembly from which to register command types.</param>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    public CommandModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        foreach (var registrableCommandConstruct in assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IRegistrableCommandConstruct))))
        {
            _messageRegistry.Register(registrableCommandConstruct);
        }

        return this;
    }
}