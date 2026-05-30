using System;
using LiteBus.Inbox;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers Microsoft hosting services for the inbox processor loop.
/// </summary>
public sealed class InboxProcessorHostingModule : IModule, IRequires<InboxModule>
{
    /// <summary>
    ///     Gets the optional callback that configures poll interval, startup delay, and adaptive polling.
    /// </summary>
    private readonly Action<InboxProcessorHostOptions>? _configure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessorHostingModule" /> class.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    public InboxProcessorHostingModule(Action<InboxProcessorHostOptions>? configure = null)
    {
        _configure = configure;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hostOptions = new InboxProcessorHostOptions();
        _configure?.Invoke(hostOptions);

        var hostState = new InboxProcessorHostState();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InboxProcessorHostOptions),
            hostOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.RegisterHostedService(typeof(InboxProcessorHostedService));
    }
}
