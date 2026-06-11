using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Dispatch.InProcess;

/// <summary>
///     Registers the command inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderCommandDispatchExtensions
{
    /// <summary>
    ///     Registers the in-process command inbox dispatcher as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseCommandInboxDispatcher(this InboxModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterDispatcher(new CommandInboxDispatchModule());
    }
}