using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Configures handler and message type registration for the messaging module.
/// </summary>
public sealed class MessageModuleBuilder
{
    /// <summary>
    ///     Gets the shared message registry used to register handlers and message types.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry shared across LiteBus modules.</param>
    /// <param name="contracts">The message contract registry for persisted inbox and outbox messages.</param>
    public MessageModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer used to register stable persisted contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets the optional time provider registered for messaging and dependent modules.
    /// </summary>
    internal TimeProvider? TimeProvider { get; private set; }

    /// <summary>
    ///     Registers the <see cref="TimeProvider" /> instance exposed through dependency injection.
    /// </summary>
    /// <param name="timeProvider">The time provider to register. When omitted at build time, <see cref="TimeProvider.System" /> is used.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        return this;
    }

    /// <summary>
    ///     Registers a message or handler type with the message registry.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder Register<T>()
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    /// <summary>
    ///     Registers a message or handler type with the message registry.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder Register(Type type)
    {
        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all applicable types from an assembly with the message registry.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            _messageRegistry.Register(type);
        }

        Contracts.AddFromAssembly(assembly);

        return this;
    }
}
