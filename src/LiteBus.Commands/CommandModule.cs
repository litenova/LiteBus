using System;
using System.Linq;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Idempotency;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     Module for configuring command handling infrastructure.
///     Depends on the messaging module for core messaging functionality.
/// </summary>
public sealed class CommandModule : ICompositeModule, IRequires<MessageModule>
{
    /// <summary>
    ///     Gets the configuration action invoked when child modules are declared.
    /// </summary>
    private readonly Action<CommandModuleBuilder> _builder;

    /// <summary>
    ///     The builder populated when child modules are declared, and read again when the module builds.
    /// </summary>
    private CommandModuleBuilder? _moduleBuilder;

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

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(registerChild);

        _moduleBuilder = new CommandModuleBuilder();
        _builder(_moduleBuilder);

        foreach (var childModule in _moduleBuilder.CollectSubModules())
        {
            registerChild(childModule);
        }
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

        // DeclareChildren always runs first, so the builder is populated by the time the module builds.
        var moduleBuilder = _moduleBuilder ?? throw new InvalidOperationException(
            "CommandModule.Build ran before DeclareChildren. The module registry declares children during Register.");

        moduleBuilder.ApplyContracts(contractRegistry);

        // AddAuditing on the messaging module selects the axes, so a consumer configures the feature once instead of
        // repeating the decision here. EnableAuditing stays the primitive, and registering the same handler twice is a
        // no-op in the registry.
        if (configuration.TryGetContext<AuditingComposition>(out var auditing) && auditing?.Commands == true)
        {
            moduleBuilder.EnableAuditing();
        }

        // Applied after every source of registrations has run, including the axis selection above, because the
        // builder now queues registrations rather than writing them straight through to the registry.
        moduleBuilder.ApplyRegistrations(messageRegistry);

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

        RegisterCommandServices(configuration, moduleBuilder.UnitOfWorkType);
        // Recorded for the composition summary. Only this module knows how many messages belong to its axis, because
        // the messaging registry does not reference the axis contracts.
        if (configuration.TryGetContext<LiteBusCompositionSummary>(out var summary) && summary is not null)
        {
            summary.RecordAxis("commands", ModuleRegistration.CountAxisMessages<ICommand>(messageRegistry));
        }

        ModuleRegistration.RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Registers command-specific services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="unitOfWorkType">The unit of work implementation type registered through the builder, or <see langword="null" /> when the application commits its own way.</param>
    private static void RegisterCommandServices(IModuleConfiguration configuration, Type? unitOfWorkType)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandMediator),
            typeof(CommandMediator)));

        if (unitOfWorkType is null)
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            unitOfWorkType,
            unitOfWorkType,
            InstanceLifetime.Scoped));

        // The completion handler takes the concrete type, so each axis commits its own even when commands and queries
        // name different implementations. The shared interface resolves to the first module's registration instead of
        // failing composition with a conflicting binding.
        if (configuration.DependencyRegistry.Any(static descriptor => descriptor.DependencyType == typeof(IUnitOfWork)))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IUnitOfWork),
            unitOfWorkType,
            InstanceLifetime.Scoped));
    }
}
