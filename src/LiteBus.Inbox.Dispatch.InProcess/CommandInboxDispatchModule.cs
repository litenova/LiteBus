using System;
using System.Linq;
using LiteBus.Commands;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.Dispatch.InProcess;

/// <summary>
///     Module that registers <see cref="CommandInboxDispatcher" /> as <see cref="IInboxDispatcher" />.
/// </summary>
/// <remarks>
///     Register this module through <see cref="InboxModuleBuilderCommandDispatchExtensions.UseInProcessDispatch" />
///     inside
///     <c>AddInboxModule</c> after <c>AddCommandModule</c>. The inbox module supplies contract registration and the
///     command module supplies <c>ICommandMediator</c> from <c>LiteBus.Commands.Abstractions</c>.
/// </remarks>
public sealed class CommandInboxDispatchModule :
    IInboxDispatcherModule,
    IRequires<InboxModule>,
    IRequires<CommandModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDispatcher),
            typeof(CommandInboxDispatcher)));
    }
}
