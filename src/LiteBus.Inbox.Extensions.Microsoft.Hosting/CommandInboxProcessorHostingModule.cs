using System;
using LiteBus.Inbox;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers Microsoft hosting services for the command inbox processor loop.
/// </summary>
public sealed class CommandInboxProcessorHostingModule : IModule, IRequires<CommandInboxModule>
{
    /// <summary>
    ///     Gets the optional callback that configures poll interval, startup delay, and adaptive polling.
    /// </summary>
    private readonly Action<CommandInboxProcessorHostOptions>? _configure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxProcessorHostingModule" /> class.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    public CommandInboxProcessorHostingModule(Action<CommandInboxProcessorHostOptions>? configure = null)
    {
        _configure = configure;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hostOptions = new CommandInboxProcessorHostOptions();
        _configure?.Invoke(hostOptions);

        var hostState = new CommandInboxProcessorHostState();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(CommandInboxProcessorHostOptions),
            hostOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(CommandInboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.RegisterHostedService(typeof(CommandInboxProcessorHostedService));
    }
}
