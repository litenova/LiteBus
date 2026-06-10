using System;
using System.Linq;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.Dispatch.InProcess;

/// <summary>
///     Module that registers <see cref="CommandInboxDispatcher" /> as <see cref="IInboxDispatcher" />.
/// </summary>
/// <remarks>
///     Register this module through <see cref="InboxModuleBuilderCommandDispatchExtensions.UseCommandInboxDispatcher" /> inside
///     <c>AddInboxModule</c> after <c>AddCommandModule</c>. The inbox module supplies contract registration and the
///     command module supplies <c>ICommandMediator</c> from <c>LiteBus.Commands.Abstractions</c>.
/// </remarks>
public sealed class CommandInboxDispatchModule : IInboxDispatcherModule, IRequires<InboxModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IInboxDispatcher)))
        {
            throw new LiteBusConfigurationException(
                "An IInboxDispatcher is already registered. Register only one inbox dispatcher implementation.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDispatcher),
            typeof(CommandInboxDispatcher)));
    }
}
