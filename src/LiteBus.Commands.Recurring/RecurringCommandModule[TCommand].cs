using System;
using LiteBus.Runtime.Abstractions;

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

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandDeclaration<TCommand>),
            _declaration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandBackgroundService<TCommand>),
            typeof(RecurringCommandBackgroundService<TCommand>)));

        configuration.RegisterBackgroundService(typeof(RecurringCommandBackgroundService<TCommand>));
    }
}
