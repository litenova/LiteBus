using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Module for configuring messaging infrastructure components.
///     This is a foundational module that other modules depend on.
/// </summary>
public sealed class MessageModule : IModule
{
    /// <summary>
    ///     The configuration callback invoked while the messaging module is built.
    /// </summary>
    private readonly Action<MessageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the message module.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public MessageModule(Action<MessageModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        // Create or get the message registry - this will be shared across all messaging-related modules.
        var messageRegistry = MessageRegistryAccessor.Instance;
        var messageContractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        var startIndex = messageRegistry.Handlers.Count;

        configuration.SetContext(messageRegistry);

        // Configure the message module using the builder.
        var moduleBuilder = new MessageModuleBuilder(messageRegistry, messageContractRegistry);
        _builder(moduleBuilder);

        // Register core messaging services.
        RegisterMessagingServices(configuration, messageRegistry, messageContractRegistry);
        RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Registers core messaging services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry instance.</param>
    /// <param name="messageContractRegistry">The message contract registry instance.</param>
    private static void RegisterMessagingServices(
        IModuleConfiguration configuration,
        IMessageRegistry messageRegistry,
        MessageContractRegistry messageContractRegistry)
    {
        // Register message registry as singleton.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageRegistry),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageSerializer),
            typeof(SystemTextJsonMessageSerializer)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TimeProvider),
            TimeProvider.System));

        // Register message mediator as transient.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMediator),
            typeof(MessageMediator)));

        // Register execution context accessor as transient factory.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IExecutionContext),
            _ => AmbientExecutionContext.Current));
    }

    /// <summary>
    ///     Registers handler types that were added to the message registry during module building.
    /// </summary>
    /// <param name="configuration">The module configuration to register handlers with.</param>
    /// <param name="messageRegistry">The message registry containing the handlers.</param>
    /// <param name="startIndex">The index from which to start registering new handlers.</param>
    private static void RegisterNewHandlers(IModuleConfiguration configuration, IMessageRegistry messageRegistry, int startIndex)
    {
        var newHandlers = messageRegistry.Handlers.Skip(startIndex);

        foreach (var handlerDescriptor in newHandlers)
        {
            var handlerType = handlerDescriptor.HandlerType;

            if (handlerType is { IsClass: true, IsAbstract: false })
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(handlerType, handlerType));
            }
        }
    }
}