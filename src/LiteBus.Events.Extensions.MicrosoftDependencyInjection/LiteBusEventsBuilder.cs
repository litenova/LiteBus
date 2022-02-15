using System;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

public class LiteBusEventBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusEventBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    /// <summary>
    ///     Register a event or event handler (i.e., handlers, pre-handlers, post-handlers, error-handlers)
    /// </summary>
    /// <typeparam name="T">The type of event or event handler</typeparam>
    /// <returns>The instance of <see cref="LiteBusEventBuilder"/></returns>
    /// <exception cref="NotSupportedException">In case the given type is neither event nor event handler</exception>
    public LiteBusEventBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Register a event or event handler (i.e., handlers, pre-handlers, post-handlers, error-handlers)
    /// </summary>
    /// <param name="type">The type of event or event handler</param>
    /// <returns>The instance of <see cref="LiteBusEventBuilder"/></returns>
    /// <exception cref="NotSupportedException">In case the given type is neither event nor event handler</exception>
    public LiteBusEventBuilder Register(Type type)
    {
        if (type.IsAssignableTo(typeof(IEventHandler)) || type.IsAssignableTo(typeof(IEvent)))
        {
            _messageRegistry.Register(type);
            return this;
        }

        throw new NotSupportedException($"The type of '{type.Name}' cannot be registered");
    }

    /// <summary>
    ///     Registers events and event handlers found in the given assembly
    /// </summary>
    /// <param name="assembly">The assembly to search</param>
    /// <returns>The instance of <see cref="LiteBusEventBuilder"/></returns>
    public LiteBusEventBuilder RegisterFrom(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAssignableTo(typeof(IEventHandler)) || type.IsAssignableTo(typeof(IEvent)))
            {
                _messageRegistry.Register(type);
            }
        }

        return this;
    }
}