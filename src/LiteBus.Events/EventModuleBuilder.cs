using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Events;

/// <summary>
///     Builder class for registering event types in the message registry.
/// </summary>
public sealed class EventModuleBuilder
{
    /// <summary>
    ///     Event handler contracts accepted by event-specific discovery.
    /// </summary>
    private static readonly HashSet<Type> HandlerContracts =
    [
        typeof(IEventHandler<>),
        typeof(IEventPreHandler),
        typeof(IEventPreHandler<>),
        typeof(IEventPostHandler),
        typeof(IEventPostHandler<>),
        typeof(IEventErrorHandler),
        typeof(IEventErrorHandler<>)
    ];

    /// <summary>
    ///     Gets the message registry to which event types are registered.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry to which events will be registered.</param>
    /// <param name="contracts">The contract writer used during module configuration.</param>
    public EventModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        ArgumentNullException.ThrowIfNull(contracts);
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer for persisted event contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Registers an event type for the message registry.
    /// </summary>
    /// <typeparam name="T">The event or event handler type to register.</typeparam>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Registers an event type for the message registry.
    /// </summary>
    /// <param name="type">The event or event handler type to register.</param>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!IsEventConstruct(type))
        {
            throw new LiteBusNotSupportedException($"The given type '{type.Name}' is not an event construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all concrete event and event handler types from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly from which to register event types.</param>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly" /> is <see langword="null" />.</exception>
    public EventModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var registrableEventConstruct in assembly.GetTypes()
                     .Where(static type => type is { IsClass: true, IsAbstract: false } && IsEventConstruct(type)))
        {
            _messageRegistry.Register(registrableEventConstruct);
        }

        return this;
    }

    /// <summary>
    ///     Determines whether a type is an event message or implements an event handler contract.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type belongs to the event axis; otherwise, <see langword="false" />.</returns>
    internal static bool IsEventConstruct(Type type)
    {
        if (typeof(IEvent).IsAssignableFrom(type))
        {
            return true;
        }

        return type.GetInterfaces().Any(static contract =>
        {
            var contractDefinition = contract.IsGenericType ? contract.GetGenericTypeDefinition() : contract;
            return HandlerContracts.Contains(contractDefinition);
        });
    }
}
