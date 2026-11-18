using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Batching;
using LiteBus.Commands.Idempotency;
using LiteBus.Commands.UnitOfWork;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Idempotency;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    ///     The child modules already declared through the module registry, compared by reference.
    /// </summary>
    private readonly List<IModule> _declaredChildren = [];

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
    ///     Runs an additional configuration callback on the existing command builder and declares new child modules.
    /// </summary>
    /// <param name="builderAction">The additional command configuration callback.</param>
    /// <param name="registerChild">Registers each newly declared child module with the module registry.</param>
    internal void Configure(Action<CommandModuleBuilder> builderAction, Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(builderAction);
        ArgumentNullException.ThrowIfNull(registerChild);

        var moduleBuilder = _moduleBuilder ?? throw new InvalidOperationException(
            "CommandModule.Configure ran before DeclareChildren. The module registry declares children during Register.");

        builderAction(moduleBuilder);

        foreach (var childModule in moduleBuilder.CollectSubModules())
        {
            if (IsAlreadyDeclared(childModule))
            {
                continue;
            }

            registerChild(childModule);
            _declaredChildren.Add(childModule);
        }
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
            _declaredChildren.Add(childModule);
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

        RegisterCommandServices(configuration, moduleBuilder);
        // Recorded for the composition summary. Only this module knows how many messages belong to its axis, because
        // the messaging registry does not reference the axis contracts.
        if (configuration.TryGetContext<LiteBusCompositionSummary>(out var summary) && summary is not null)
        {
            summary.RecordAxis("commands", ModuleRegistration.CountAxisMessages<ICommand>(messageRegistry));
        }

        ModuleRegistration.RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Determines whether a child module was already declared through the module registry.
    /// </summary>
    /// <param name="childModule">The child module collected from the builder.</param>
    /// <returns><see langword="true" /> when the same instance was already declared; otherwise, <see langword="false" />.</returns>
    private bool IsAlreadyDeclared(IModule childModule)
    {
        foreach (var declared in _declaredChildren)
        {
            if (ReferenceEquals(declared, childModule))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Registers command-specific services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="moduleBuilder">The command module builder holding the unit-of-work, auditing and idempotency selections.</param>
    private static void RegisterCommandServices(IModuleConfiguration configuration, CommandModuleBuilder moduleBuilder)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(moduleBuilder);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandMediator),
            typeof(CommandMediator)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandBatchMediator),
            typeof(CommandBatchMediator)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(CommandBatchContext),
            typeof(CommandBatchContext),
            InstanceLifetime.Scoped));

        if (moduleBuilder.IdempotencyEnabled)
        {
            // The concrete registration is the one instance per batch scope; the completion handlers add to it and the
            // batch reads it through the participant collection, so both must resolve the same object.
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(CommandBatchIdempotencyParticipant),
                typeof(CommandBatchIdempotencyParticipant),
                InstanceLifetime.Scoped));

            configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                typeof(ICommandBatchParticipant),
                serviceProvider => serviceProvider.GetService(typeof(CommandBatchIdempotencyParticipant))
                    ?? throw new LiteBusDependencyResolutionException(typeof(CommandBatchIdempotencyParticipant)),
                InstanceLifetime.Scoped));
        }

        if (moduleBuilder.AuditingEnabled)
        {
            // The concrete registration is the one instance per batch scope; the completion handlers add to it and the
            // batch reads it through the participant collection, so both must resolve the same object.
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(CommandBatchAuditParticipant),
                typeof(CommandBatchAuditParticipant),
                InstanceLifetime.Scoped));

            configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                typeof(ICommandBatchParticipant),
                serviceProvider => serviceProvider.GetService(typeof(CommandBatchAuditParticipant))
                    ?? throw new LiteBusDependencyResolutionException(typeof(CommandBatchAuditParticipant)),
                InstanceLifetime.Scoped));
        }

        var unitOfWorkType = moduleBuilder.UnitOfWorkType;

        if (unitOfWorkType is null)
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            unitOfWorkType,
            unitOfWorkType,
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(CommandUnitOfWorkRegistration),
            new CommandUnitOfWorkRegistration(unitOfWorkType)));

        // The completion handler takes the concrete type, so each axis commits its own even when commands and queries
        // name different implementations. The shared interface resolves to the first module's registration instead of
        // failing composition with a conflicting binding.
        if (configuration.DependencyRegistry.Any(static descriptor => descriptor.DependencyType == typeof(IUnitOfWork)))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IUnitOfWork),
            // Resolve the concrete registration, so IUnitOfWork is the same scoped instance the completion handler commits
            // and the boundary pre-handler begins. A separate type mapping would create a second instance with no
            // transaction, which an application resolving IUnitOfWork (for example as its transaction provider) would see.
            serviceProvider => serviceProvider.GetService(unitOfWorkType)
                ?? throw new LiteBusDependencyResolutionException(unitOfWorkType),
            InstanceLifetime.Scoped));
    }
}
