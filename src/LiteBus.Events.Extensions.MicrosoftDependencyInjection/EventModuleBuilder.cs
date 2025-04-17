using System;
using System.Linq;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Builder class for registering event types in the message registry.
/// </summary>
public sealed class EventModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry to which events will be registered.</param>
    public EventModuleBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    /// <summary>
    ///     Registers an event type for the message registry.
    /// </summary>
    /// <typeparam name="T">The type of event to register, which must implement <see cref="IRegistrableEventConstruct" />.</typeparam>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder Register<T>() where T : IRegistrableEventConstruct
    {
        _messageRegistry.Register(typeof(T));
        return this;
    }

    /// <summary>
    ///     Registers an event type for the message registry.
    /// </summary>
    /// <param name="type">The type of event to register, which must implement <see cref="IRegistrableEventConstruct" />.</param>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder Register(Type type)
    {
        if (!type.IsAssignableTo(typeof(IRegistrableEventConstruct)))
        {
            throw new NotSupportedException($"The given type '{type.Name}' is not an event construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all event types from the specified assembly that implement <see cref="IRegistrableEventConstruct" />.
    /// </summary>
    /// <param name="assembly">The assembly from which to register event types.</param>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        foreach (var registrableEventConstruct in assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IRegistrableEventConstruct))))
        {
            _messageRegistry.Register(registrableEventConstruct);
        }

        return this;
    }
}