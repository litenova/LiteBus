using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Dispatch.InProcess;

/// <summary>
///     Registers the in-process inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderInProcessExtensions
{
    /// <summary>
    ///     Registers the in-process inbox dispatcher as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseInProcessDispatcher(this InboxModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterDispatcher(new InProcessInboxDispatchModule());
    }
}
