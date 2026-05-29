using System;
using LiteBus.Outbox;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers Microsoft hosting services for the outbox processor loop.
/// </summary>
public sealed class OutboxProcessorHostingModule : IModule, IRequires<OutboxModule>
{
    /// <summary>
    ///     Gets the optional callback that configures poll interval, startup delay, and adaptive polling.
    /// </summary>
    private readonly Action<OutboxProcessorHostOptions>? _configure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorHostingModule" /> class.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    public OutboxProcessorHostingModule(Action<OutboxProcessorHostOptions>? configure = null)
    {
        _configure = configure;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hostOptions = new OutboxProcessorHostOptions();
        _configure?.Invoke(hostOptions);

        var hostState = new OutboxProcessorHostState();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxProcessorHostOptions),
            hostOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.RegisterHostedService(typeof(OutboxProcessorHostedService));
    }
}
