using System;
using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Registers the background loop that sends one command per tenant repeatedly.
/// </summary>
/// <typeparam name="TCommand">The command type sent once per tenant on each pass.</typeparam>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderRecurringExtensions.AddRecurringForEachTenant{TCommand}(CommandModuleBuilder, System.Func{System.String, TCommand}, System.Action{RecurringCommandHostOptions})" />, so the loop is composed inside
///     <c>AddCommands</c> and cannot be registered without the axis that mediates it.
/// </remarks>
public sealed class RecurringTenantSweepModule<TCommand> : IModule, IRequires<CommandModule>
    where TCommand : notnull
{
    /// <summary>
    ///     The declaration carrying the tenant factory, the tenant source, and the host options.
    /// </summary>
    private readonly RecurringTenantSweepDeclaration<TCommand> _declaration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringTenantSweepModule{TCommand}" /> class.
    /// </summary>
    /// <param name="declaration">The declaration carrying the tenant factory, the tenant source, and the host options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaration" /> is <see langword="null" />.</exception>
    public RecurringTenantSweepModule(RecurringTenantSweepDeclaration<TCommand> declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _declaration = declaration;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _declaration.HostOptions.Validate();

        if (_declaration.HostOptions.RequireLease)
        {
            // The lease registers through a sibling child module that may build after this one, so the check is
            // deferred until every module has built. It still fails at composition time: a loop that silently ran on
            // every instance after asking for single-flight would be worse than a boot failure.
            configuration.RegisterCompositionValidation(() => ThrowIfLeaseMissing(configuration));
        }

        if (_declaration.TenantSource is null && _declaration.TenantSourceType is null)
        {
            // The tenant source registers through a sibling child module that may build after this one, so the check
            // is deferred until every module has built. It still fails at composition time: a sweep with no source
            // would fail its first pass instead.
            configuration.RegisterCompositionValidation(() => ThrowIfTenantSourceMissing(configuration));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringTenantSweepDeclaration<TCommand>),
            _declaration));

        if (_declaration.TenantSourceType is not null)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                _declaration.TenantSourceType,
                _declaration.TenantSourceType,
                InstanceLifetime.Scoped));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringTenantSweepBackgroundService<TCommand>),
            typeof(RecurringTenantSweepBackgroundService<TCommand>)));

        configuration.RegisterBackgroundService(typeof(RecurringTenantSweepBackgroundService<TCommand>));
    }

    /// <summary>
    ///     Throws when the loop requires single-flight but no lease implementation was registered.
    /// </summary>
    /// <param name="configuration">The module configuration holding the completed dependency registry.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     No <see cref="IRecurringCommandLease" /> registration is present.
    /// </exception>
    private static void ThrowIfLeaseMissing(IModuleConfiguration configuration)
    {
        var leaseRegistered = configuration.DependencyRegistry
            .Any(static descriptor => descriptor.DependencyType == typeof(IRecurringCommandLease));

        if (!leaseRegistered)
        {
            throw new LiteBusConfigurationException(
                $"The recurring tenant sweep '{typeof(TCommand).FullName}' requires a lease "
                + "(RecurringCommandHostOptions.RequireLease is true) but no IRecurringCommandLease is registered. "
                + "Register one through CommandModuleBuilder.RegisterChildModule, for example with "
                + "UsePostgreSqlRecurringLease from LiteBus.Commands.Recurring.Storage.PostgreSql.");
        }
    }

    /// <summary>
    ///     Throws when the sweep resolves its tenant source from the registry but none was registered.
    /// </summary>
    /// <param name="configuration">The module configuration holding the completed dependency registry.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     No <see cref="IRecurringTenantSource" /> registration is present.
    /// </exception>
    private static void ThrowIfTenantSourceMissing(IModuleConfiguration configuration)
    {
        var sourceRegistered = configuration.DependencyRegistry
            .Any(static descriptor => descriptor.DependencyType == typeof(IRecurringTenantSource));

        if (!sourceRegistered)
        {
            throw new LiteBusConfigurationException(
                $"The recurring tenant sweep '{typeof(TCommand).FullName}' has no tenant source. "
                + "Register an IRecurringTenantSource through CommandModuleBuilder.RegisterChildModule, "
                + "or pass a source instance or type to AddRecurringForEachTenant.");
        }
    }
}
