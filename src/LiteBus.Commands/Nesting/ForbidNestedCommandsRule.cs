using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Nesting;

/// <summary>
///     Denies a command sent from inside another command's handling.
/// </summary>
/// <remarks>
///     <para>
///         Each command commits in its own pipeline, so a nested send commits before the outer command finishes: when
///         the outer command then fails, the inner change is already stored with nothing to undo it. Denying the send
///         keeps one command per commit; the reaction to the outer command's own outcome event is where the follow-up
///         work belongs.
///     </para>
///     <para>
///         Only an inner command under a command ancestor is denied. A query nested in a command stays allowed, an
///         event published in process from a command is allowed, and a command sent from that event's handler is
///         denied, because the command ancestor is found through the chain. Durable dispatch has no enclosing
///         mediation, so outbox and inbox delivery never nest.
///     </para>
/// </remarks>
public sealed class ForbidNestedCommandsRule : IMediationNestingRule
{
    /// <summary>
    ///     The code carried by the denial: a command was sent from inside another command's handling.
    /// </summary>
    public const string NestedCommand = "commands.nested-command";

    /// <inheritdoc />
    public Verdict Evaluate(MediationNestingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!typeof(ICommand).IsAssignableFrom(context.MessageType))
        {
            return Verdict.Allow;
        }

        foreach (var ancestor in context.Ancestors())
        {
            var ancestorMessageType = ancestor.MessageType;

            if (ancestorMessageType is not null && typeof(ICommand).IsAssignableFrom(ancestorMessageType))
            {
                return Verdict.Deny(
                    $"Command '{context.MessageType.FullName ?? context.MessageType.Name}' cannot be sent from inside "
                    + $"command '{ancestorMessageType.FullName ?? ancestorMessageType.Name}': each command commits in "
                    + "its own pipeline, so the nested send would commit before the outer command finishes.",
                    NestedCommand);
            }
        }

        return Verdict.Allow;
    }
}
