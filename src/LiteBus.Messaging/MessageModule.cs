using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        if (!configuration.DependencyRegistry.Any(static descriptor =>
                descriptor.DependencyType == typeof(IMessageDispatchScopeFactory)))
        {
            throw new LiteBusConfigurationException(
                "Message dispatch requires an IMessageDispatchScopeFactory. " +
                "Use a supported container adapter or explicitly register RootMessageDispatchScopeFactory in a custom host.");
        }

        // Create or get the message registry shared by all messaging-related modules in this configuration.
        var messageRegistry = configuration.GetOrCreateContext<IMessageRegistry>(() => new MessageRegistry());
        var messageContractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        var startIndex = messageRegistry.Handlers.Count;

        // Configure the message module using the builder.
        var moduleBuilder = new MessageModuleBuilder(messageRegistry, messageContractRegistry);
        _builder(moduleBuilder);

        // Register core messaging services.
        RegisterMessagingServices(
            configuration,
            messageRegistry,
            messageContractRegistry,
            moduleBuilder.TimeProvider,
            moduleBuilder.AuditOutcomeMapper);
        RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Registers core messaging services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry instance.</param>
    /// <param name="messageContractRegistry">The message contract registry instance.</param>
    /// <param name="timeProvider">The optional time provider registered for messaging services.</param>
    /// <param name="auditOutcomeMapper">The optional audit outcome mapper registered for the audit writer.</param>
    private static void RegisterMessagingServices(
        IModuleConfiguration configuration,
        IMessageRegistry messageRegistry,
        MessageContractRegistry messageContractRegistry,
        TimeProvider? timeProvider,
        IAuditOutcomeMapper? auditOutcomeMapper)
    {
        // Register message registry as singleton.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageRegistry),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageReader),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageWriter),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IContractReader),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IContractWriter),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageSerializer),
            typeof(SystemTextJsonMessageSerializer)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TimeProvider),
            timeProvider ?? TimeProvider.System));

        // The audit scope is stateless: it reads and writes the ambient execution context, so a singleton is correct
        // and remains safe under concurrency because two mediations never share an execution context.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditScope),
            new AmbientAuditScope()));

        // Applications supply their own mapper to record a refusal exception as a denial rather than a failure.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditOutcomeMapper),
            auditOutcomeMapper ?? new DefaultAuditOutcomeMapper()));

        // Resolved only when an axis enables auditing, which also requires the application to register an IAuditTrail.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditRecordWriter),
            typeof(AuditRecordWriter),
            InstanceLifetime.Scoped));

        // Register message mediator as transient.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMediator),
            typeof(MessageMediator)));
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
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    handlerType,
                    handlerType,
                    InstanceLifetime.Scoped));
            }
        }
    }
}
