using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Registers the background loop that sends one command repeatedly.
/// </summary>
/// <typeparam name="TCommand">The command type sent on each pass.</typeparam>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderRecurringExtensions.AddRecurring{TCommand}(CommandModuleBuilder, System.Func{TCommand}, System.Action{RecurringCommandHostOptions}, System.Func{TCommand, LiteBus.Messaging.Abstractions.ExecutionIdentity})" />, so the loop is composed inside
///     <c>AddCommands</c> and cannot be registered without the axis that mediates it.
/// </remarks>
public sealed class RecurringCommandModule<TCommand> : IModule, IRequires<CommandModule>
    where TCommand : notnull
{
    /// <summary>
    ///     The declaration carrying the command factory and the host options.
    /// </summary>
    private readonly RecurringCommandDeclaration<TCommand> _declaration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandModule{TCommand}" /> class.
    /// </summary>
    /// <param name="declaration">The declaration carrying the command factory and the host options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaration" /> is <see langword="null" />.</exception>
    public RecurringCommandModule(RecurringCommandDeclaration<TCommand> declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _declaration = declaration;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _declaration.HostOptions.Validate();

        ThrowIfTenantRequiredWithoutIdentity(configuration);

        if (_declaration.HostOptions.RequireLease)
        {
            // The lease registers through a sibling child module that may build after this one, so the check is
            // deferred until every module has built. It still fails at composition time: a loop that silently ran on
            // every instance after asking for single-flight would be worse than a boot failure.
            configuration.RegisterCompositionValidation(() => ThrowIfLeaseMissing(configuration));
        }

        configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
            typeof(IRecurringOccurrenceStore),
            typeof(InMemoryRecurringOccurrenceStore),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandDeclaration<TCommand>),
            _declaration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandBackgroundService<TCommand>),
            typeof(RecurringCommandBackgroundService<TCommand>)));

        configuration.RegisterBackgroundService(typeof(RecurringCommandBackgroundService<TCommand>));
    }

    /// <summary>
    ///     Throws when the command requires a tenant but the registration supplies no identity to send with.
    /// </summary>
    /// <param name="configuration">The module configuration holding the message registry.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     <typeparamref name="TCommand" /> declares <see cref="TenantRequiredDeclaration" /> and the registration
    ///     supplies no identity factory.
    /// </exception>
    /// <remarks>
    ///     The command module builds before this child module, so its registrations are already applied and the
    ///     declaration reads from committed metadata. A command the registry does not hold yet cannot be judged
    ///     here; its missing registration fails elsewhere.
    /// </remarks>
    private void ThrowIfTenantRequiredWithoutIdentity(IModuleConfiguration configuration)
    {
        if (_declaration.IdentityOf is not null)
        {
            return;
        }

        if (!configuration.TryGetContext<IMessageRegistry>(out var registry) || registry is null)
        {
            return;
        }

        var descriptor = registry.Find(typeof(TCommand));

        if (descriptor is null)
        {
            return;
        }

        if (!descriptor.Metadata.TryGet<TenancyDeclaration>(out var tenancy))
        {
            return;
        }

        if (tenancy is not TenantRequiredDeclaration)
        {
            return;
        }

        throw new LiteBusConfigurationException(
            $"The recurring command '{typeof(TCommand).FullName}' requires a tenant (TenantRequiredDeclaration) "
            + $"but the recurring registration '{typeof(RecurringCommandModule<TCommand>).FullName}' supplies no identity. "
            + "Pass an identity factory to AddRecurring, or use AddRecurringForEachTenant so every send carries a tenant.");
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
                $"The recurring command '{typeof(TCommand).FullName}' requires a lease "
                + "(RecurringCommandHostOptions.RequireLease is true) but no IRecurringCommandLease is registered. "
                + "Register one through CommandModuleBuilder.RegisterChildModule, for example with "
                + "UsePostgreSqlRecurringLease from LiteBus.Commands.Recurring.Storage.PostgreSql.");
        }
    }
}
