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
            moduleBuilder.AuditOutcomeMapper,
            moduleBuilder.AuditTrail,
            moduleBuilder.AuditTrailLifetime);
        RegisterNewHandlers(configuration, messageRegistry, startIndex);

        // Deferred because this module is foundational: the commands and queries the requirement applies to are
        // registered by modules that build after it.
        if (moduleBuilder.RequiredDeclarations.Count > 0)
        {
            var required = moduleBuilder.RequiredDeclarations;
            configuration.RegisterCompositionValidation(
                () => RequiredDeclarationValidator.Validate(messageRegistry, required));
        }
    }

    /// <summary>
    ///     Registers core messaging services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry instance.</param>
    /// <param name="messageContractRegistry">The message contract registry instance.</param>
    /// <param name="timeProvider">The optional time provider registered for messaging services.</param>
    /// <param name="auditOutcomeMapper">The optional audit outcome mapper registered for the audit writer.</param>
    /// <param name="auditTrail">The audit trail registered through the builder, when the application supplied one there.</param>
    /// <param name="auditTrailLifetime">The lifetime an audit trail implementation type is registered with.</param>
    private static void RegisterMessagingServices(
        IModuleConfiguration configuration,
        IMessageRegistry messageRegistry,
        MessageContractRegistry messageContractRegistry,
        TimeProvider? timeProvider,
        IAuditOutcomeMapper? auditOutcomeMapper,
        object? auditTrail,
        InstanceLifetime auditTrailLifetime)
    {
        // Register message registry as singleton.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageRegistry),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageReader),
            messageRegistry));

        // The read-only view applications use to read their own declarations, so a generic guard does not have to
        // navigate descriptors to find the metadata bag.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMetadataAccessor),
            new MessageMetadataAccessor(messageRegistry)));

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

        RegisterAuditTrail(configuration, auditTrail, auditTrailLifetime);

        // Registered through a factory rather than by type on purpose. The writer needs an IAuditTrail that only an
        // application auditing its messages registers, and a by-type registration makes a container running
        // ValidateOnBuild fail at startup for every application that is not auditing at all. The factory defers the
        // lookup to the first audited mediation and names the fix when it is missing; the litebus.audit.trail probe
        // reports it earlier still.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditRecordWriter),
            CreateAuditRecordWriter,
            InstanceLifetime.Scoped));

        // Register message mediator as transient.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMediator),
            typeof(MessageMediator)));
    }

    /// <summary>
    ///     Registers the audit trail the builder was given, when it was given one.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="auditTrail">The trail instance or implementation type, or null when the application registers it itself.</param>
    /// <param name="lifetime">The lifetime an implementation type is registered with; an instance is always a singleton.</param>
    /// <remarks>
    ///     An application may register the trail with its own container instead, which is what the
    ///     <c>litebus.audit.trail</c> probe checks for. Configuring it here keeps the whole feature on one builder.
    /// </remarks>
    private static void RegisterAuditTrail(
        IModuleConfiguration configuration,
        object? auditTrail,
        InstanceLifetime lifetime)
    {
        switch (auditTrail)
        {
            case null:
                return;

            case Type trailType:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IAuditTrail),
                    trailType,
                    lifetime));
                return;

            default:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IAuditTrail),
                    auditTrail));
                return;
        }
    }

    /// <summary>
    ///     Creates the audit record writer, reporting a missing trail as configuration rather than a null reference.
    /// </summary>
    /// <param name="serviceProvider">The scope the writer is being resolved from.</param>
    /// <returns>The writer bound to the registered trail.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Auditing is enabled but no <see cref="IAuditTrail" /> is registered.
    /// </exception>
    private static AuditRecordWriter CreateAuditRecordWriter(IServiceProvider serviceProvider)
    {
        var trail = (IAuditTrail?) serviceProvider.GetService(typeof(IAuditTrail))
                    ?? throw new LiteBusConfigurationException(
                        "Auditing is enabled but no IAuditTrail is registered, so audit records cannot be written. "
                        + "Register one with UseAuditTrail on the messaging module builder, or with the application "
                        + "container. The litebus.audit.trail diagnostic check reports this before the first message "
                        + "arrives.");

        return new AuditRecordWriter(
            trail,
            (IMessageRegistry) serviceProvider.GetService(typeof(IMessageRegistry))!,
            (IAuditOutcomeMapper) serviceProvider.GetService(typeof(IAuditOutcomeMapper))!,
            (TimeProvider) serviceProvider.GetService(typeof(TimeProvider))!);
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
