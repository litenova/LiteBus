using System;
using System.Linq;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Idempotency;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     Module for configuring command handling infrastructure.
///     Depends on the messaging module for core messaging functionality.
/// </summary>
public sealed class CommandModule : IModule, IRequires<MessageModule>
{
    /// <summary>
    ///     Gets the configuration action invoked when the module is built.
    /// </summary>
    private readonly Action<CommandModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the command module.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public CommandModule(Action<CommandModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <summary>
    ///     Builds the command module by configuring command handlers and registering command-specific services.
    /// </summary>
    /// <param name="configuration">The module configuration containing dependency registry and shared context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var messageRegistry = configuration.GetContext<IMessageRegistry>();
        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());

        var startIndex = messageRegistry.Handlers.Count;

        var moduleBuilder = new CommandModuleBuilder(messageRegistry, contractRegistry);
        _builder(moduleBuilder);

        // AddAuditing on the messaging module selects the axes, so a consumer configures the feature once instead of
        // repeating the decision here. EnableAuditing stays the primitive, and registering the same handler twice is a
        // no-op in the registry.
        if (configuration.TryGetContext<AuditingComposition>(out var auditing) && auditing?.Commands == true)
        {
            moduleBuilder.EnableAuditing();
        }

        if (moduleBuilder.AuditingEnabled)
        {
            configuration.RegisterDiagnosticCheck(typeof(AuditTrailDiagnosticCheck), AuditTrailDiagnosticCheck.CheckName);
        }

        if (moduleBuilder.IdempotencyEnabled)
        {
            configuration.RegisterDiagnosticCheck(
                typeof(IdempotencyStoreDiagnosticCheck),
                IdempotencyStoreDiagnosticCheck.CheckName);
        }

        RegisterCommandServices(configuration);
        // Recorded for the composition summary. Only this module knows how many messages belong to its axis, because
        // the messaging registry does not reference the axis contracts.
        if (configuration.TryGetContext<LiteBusCompositionSummary>(out var summary) && summary is not null)
        {
            summary.RecordAxis("commands", CountAxisMessages(messageRegistry));
        }

        RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Registers command-specific services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    private static void RegisterCommandServices(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandMediator),
            typeof(CommandMediator)));
    }

    /// <summary>
    ///     Counts the concrete command types registered so far, for the composition summary.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <returns>The number of concrete command types.</returns>
    /// <remarks>
    ///     Abstract types and interfaces are excluded, because they are shapes rather than messages and counting them
    ///     would make the reported total disagree with the number of things that can be mediated.
    /// </remarks>
    private static int CountAxisMessages(IMessageReader reader)
    {
        var count = 0;

        foreach (var descriptor in reader)
        {
            if (descriptor.MessageType is { IsAbstract: false, IsInterface: false } &&
                typeof(ICommand).IsAssignableFrom(descriptor.MessageType))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    ///     Registers handler types that were discovered during this module's configuration.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry containing handler information.</param>
    /// <param name="startIndex">The index from which to start processing new handlers.</param>
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