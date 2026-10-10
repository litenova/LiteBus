using System;
using System.Linq;
using LiteBus.Commands.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Registers recurring command loops through <see cref="CommandModuleBuilder" />.
/// </summary>
public static class CommandModuleBuilderRecurringExtensions
{
    /// <summary>
    ///     Sends a command on a fixed interval for the life of the host.
    /// </summary>
    /// <typeparam name="TCommand">The command type sent on each pass.</typeparam>
    /// <param name="builder">The command module builder.</param>
    /// <param name="commandFactory">
    ///     Produces the command instance. Called once per pass, so a command carrying a timestamp or a fresh
    ///     identifier gets a new one each time.
    /// </param>
    /// <param name="configure">An optional action that adjusts the loop's timing and lifecycle.</param>
    /// <returns>The command module builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> or <paramref name="commandFactory" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">
    ///     <typeparamref name="TCommand" /> implements more than one <see cref="ICommand{TCommandResult}" /> contract.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         Replaces the hosted service most CQRS applications end up writing once per background job: create a
    ///         scope, resolve the mediator, send one command, sleep, back off on failure, and never let an exception
    ///         escape the loop. Each registration gets its own loop and its own options.
    ///     </para>
    ///     <para>
    ///         Registers <typeparamref name="TCommand" /> with the command module, so calling only this method is
    ///         enough for the loop to send. The registration is queued on the parent builder and applied before
    ///         registrations are flushed, because the child module builds after the parent and cannot register it.
    ///     </para>
    ///     <para>
    ///         <strong>This is a poll loop and not a scheduler.</strong> There is no cron expression and no
    ///         missed-run catch-up, and because LiteBus has no leader election the loop runs on every host instance
    ///         concurrently. That suits a command that drains work by leasing rows, where the store arbitrates between
    ///         instances; it does not suit a command that assumes it is the only one running. See
    ///         <see cref="RecurringCommandHostOptions" /> for the full boundary.
    ///     </para>
    ///     <para>
    ///         A command whose result implements <see cref="IRecurringWorkReport" /> can tell the loop to send again
    ///         immediately instead of waiting out the interval, which is what clears a backlog in one busy period
    ///         rather than one batch per interval.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddCommands(commands => commands
    ///     .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    ///     .AddRecurring(
    ///         () => new SendDueRemindersCommand { BatchSize = 50 },
    ///         host =>
    ///         {
    ///             host.Interval = TimeSpan.FromMinutes(5);
    ///             host.StartupDelay = TimeSpan.FromSeconds(30);
    ///         }));
    /// ]]></code>
    /// </example>
    public static CommandModuleBuilder AddRecurring<TCommand>(
        this CommandModuleBuilder builder,
        Func<TCommand> commandFactory,
        Action<RecurringCommandHostOptions>? configure = null)
        where TCommand : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(commandFactory);

        var hostOptions = new RecurringCommandHostOptions();
        configure?.Invoke(hostOptions);

        var commandType = typeof(TCommand);
        var declaredResultCount = commandType
            .GetInterfaces()
            .Count(static contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>));

        if (declaredResultCount > 1)
        {
            throw new LiteBusConfigurationException(
                $"The command type '{commandType.FullName}' implements more than one ICommand<> contract. "
                + "A recurring command must declare at most one result type.");
        }

        builder.Register(commandType);

        // Validates the declared contract now so a two-contract command fails at composition time.
        RecurringCommandContracts.FindResultContract(commandType);

        var declaration = new RecurringCommandDeclaration<TCommand>(commandFactory, hostOptions);

        return builder.RegisterChildModule(new RecurringCommandModule<TCommand>(declaration));
    }

    /// <summary>
    ///     Sends a parameterless command on a fixed interval for the life of the host.
    /// </summary>
    /// <typeparam name="TCommand">The command type sent on each pass. Must have a public parameterless constructor.</typeparam>
    /// <param name="builder">The command module builder.</param>
    /// <param name="configure">An optional action that adjusts the loop's timing and lifecycle.</param>
    /// <returns>The command module builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">
    ///     <typeparamref name="TCommand" /> implements more than one <see cref="ICommand{TCommandResult}" /> contract.
    /// </exception>
    /// <remarks>
    ///     The overload to reach for when the command carries no configuration of its own. See
    ///     <see cref="AddRecurring{TCommand}(CommandModuleBuilder, Func{TCommand}, Action{RecurringCommandHostOptions})" />
    ///     for the boundary this feature deliberately does not cross.
    /// </remarks>
    public static CommandModuleBuilder AddRecurring<TCommand>(
        this CommandModuleBuilder builder,
        Action<RecurringCommandHostOptions>? configure = null)
        where TCommand : notnull, new()
    {
        return builder.AddRecurring(static () => new TCommand(), configure);
    }
}
