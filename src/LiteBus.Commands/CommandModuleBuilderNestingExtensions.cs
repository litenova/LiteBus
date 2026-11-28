using System;
using LiteBus.Commands.Nesting;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     Registers command nesting rules through <see cref="CommandModuleBuilder" />.
/// </summary>
public static class CommandModuleBuilderNestingExtensions
{
    /// <summary>
    ///     Forbids a command sent from inside another command's handling.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <returns>The command module builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         Each command commits in its own pipeline, so a nested send commits before the outer command finishes:
    ///         when the outer command then fails, the inner change is already stored with nothing to undo it. With this
    ///         setting, sending a command while the enclosing mediation already handles a command throws
    ///         <see cref="NestedMediationException" /> naming both message types, before the inner pipeline starts. The
    ///         reaction to the outer command's own outcome event is where the follow-up work belongs.
    ///     </para>
    ///     <para>
    ///         Only a command under a command is denied. A query nested in a command stays allowed, an event published
    ///         through the outbox is not nesting, and an event published in process from a command is allowed; a command
    ///         sent from that event's handler is denied, because the command ancestor is found through the chain.
    ///         Calling this twice on one composition fails with the registry's duplicate-module error.
    ///     </para>
    ///     <para>
    ///         Analyzer LB1023 reports a command handler that takes <c>ICommandMediator</c> at build time; this setting
    ///         is its runtime counterpart.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddCommands(commands => commands
    ///     .RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly)
    ///     .ForbidNestedCommands());
    /// ]]></code>
    /// </example>
    public static CommandModuleBuilder ForbidNestedCommands(this CommandModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RegisterChildModule(new ForbidNestedCommandsModule());
    }
}
