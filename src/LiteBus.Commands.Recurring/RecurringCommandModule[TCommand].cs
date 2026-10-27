using System;
using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Registers the background loop that sends one command repeatedly.
/// </summary>
/// <typeparam name="TCommand">The command type sent on each pass.</typeparam>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderRecurringExtensions.AddRecurring{TCommand}(CommandModuleBuilder, System.Func{TCommand}, System.Action{RecurringCommandHostOptions})" />, so the loop is composed inside
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

        if (_declaration.HostOptions.RequireLease)
        {
            // The lease registers through a sibling child module that may build after this one, so the check is
            // deferred until every module has built. It still fails at composition time: a loop that silently ran on
            // every instance after asking for single-flight would be worse than a boot failure.
            configuration.RegisterCompositionValidation(() => ThrowIfLeaseMissing(configuration));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandDeclaration<TCommand>),
            _declaration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandBackgroundService<TCommand>),
            typeof(RecurringCommandBackgroundService<TCommand>)));

        configuration.RegisterBackgroundService(typeof(RecurringCommandBackgroundService<TCommand>));
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
