using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Modules;

namespace LiteBus.Messaging;

/// <summary>
/// Module for configuring messaging infrastructure components.
/// This is a foundational module that other modules depend on.
/// </summary>
public sealed class MessageModule : IModule
{
    private readonly Action<MessageModuleBuilder> _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageModule"/> class.
    /// </summary>
    /// <param name="builder">The configuration action for the message module.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when builder is null.</exception>
    public MessageModule(Action<MessageModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        // Create or get the message registry - this will be shared across all messaging-related modules
        var messageRegistry = (MessageRegistryAccessor.Instance);
        var startIndex = messageRegistry.Handlers.Count;

        configuration.SetContext(messageRegistry);

        // Configure the message module using the builder
        var moduleBuilder = new MessageModuleBuilder(messageRegistry);
        _builder(moduleBuilder);

        // Register core messaging services
        RegisterMessagingServices(configuration, messageRegistry);
        RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    /// Registers core messaging services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry instance.</param>
    private static void RegisterMessagingServices(
        IModuleConfiguration configuration,
        IMessageRegistry messageRegistry)
    {
        // Register message registry as singleton
        configuration.DependencyRegistry.Register(new Runtime.Dependencies.DependencyDescriptor(
            typeof(IMessageRegistry),
            messageRegistry));

        // Register message mediator as transient
        configuration.DependencyRegistry.Register(new Runtime.Dependencies.DependencyDescriptor(
            typeof(IMessageMediator),
            typeof(MessageMediator)));

        // Register execution context accessor as transient factory
        configuration.DependencyRegistry.Register(new Runtime.Dependencies.DependencyDescriptor(
            typeof(IExecutionContext),
            serviceProvider => AmbientExecutionContext.Current));
    }

    /// <summary>
    /// Registers handler types that were added to the message registry during module building.
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
                configuration.DependencyRegistry.Register(new Runtime.Dependencies.DependencyDescriptor(handlerType, handlerType));
            }
        }
    }
}