using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Hosting;

/// <summary>
///     Registers Microsoft hosting services for the command inbox processor loop.
/// </summary>
public sealed class CommandInboxProcessorHostingModule : IModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<CommandInboxProcessorHostRegistration>(out _))
        {
            throw new InvalidOperationException(
                "Command inbox processor hosting requires UseProcessorHost on AddCommandInboxModule before AddCommandInboxProcessorHosting.");
        }

        configuration.DependencyRegistry.RegisterHostedService(typeof(CommandInboxProcessorHostedService));
    }
}
