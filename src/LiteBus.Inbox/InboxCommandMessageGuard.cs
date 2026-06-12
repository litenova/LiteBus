using System;

namespace LiteBus.Inbox;

/// <summary>
///     Runtime guard that rejects command messages with result types from inbox envelope creation.
/// </summary>
internal static class InboxCommandMessageGuard
{
    /// <summary>
    ///     The full name of the generic command-with-result interface used for reflection-based detection.
    /// </summary>
    private const string CommandWithResultInterfaceFullName = "LiteBus.Commands.Abstractions.ICommand`1";

    /// <summary>
    ///     Throws when the message type implements <c>ICommand&lt;TResult&gt;</c>.
    /// </summary>
    /// <param name="messageType">The runtime message type being accepted into the inbox.</param>
    /// <exception cref="InvalidOperationException">
    ///     The message type implements <c>ICommand&lt;TResult&gt;</c> and cannot be stored through the inbox writer.
    /// </exception>
    public static void EnsureVoidCommand(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        foreach (var candidate in messageType.GetInterfaces())
        {
            if (!candidate.IsGenericType ||
                candidate.GetGenericTypeDefinition().FullName != CommandWithResultInterfaceFullName)
            {
                continue;
            }

            var resultType = candidate.GenericTypeArguments[0];
            throw new InvalidOperationException(
                $"Type '{messageType.FullName}' implements ICommand<{resultType.Name}> and cannot be stored through IInbox.AcceptAsync. " +
                "Use a void command for inbox storage or send the command immediately through ICommandMediator.");
        }
    }
}
