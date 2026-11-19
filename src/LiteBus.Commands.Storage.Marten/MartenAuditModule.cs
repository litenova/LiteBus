using System;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Registers the Marten audit hash chain: verification, checkpoints, and the time-based checkpoint loop.
/// </summary>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderMartenExtensions.UseMartenAuditChain(CommandModuleBuilder, Action{MartenAuditTrailOptions})" />,
///     so the chain is composed inside <c>AddCommands</c>. The module carries no ordering requirement: the chain
///     opens its own sessions from the application-owned <see cref="IDocumentStore" /> and needs no axis service.
///     The audit trail itself is still registered through the messaging <c>UseTrail</c> API and shares the options
///     instance this module registers.
/// </remarks>
public sealed class MartenAuditModule : IModule
{
    /// <summary>
    ///     Gets the hash chain configuration captured at registration time.
    /// </summary>
    private readonly MartenAuditTrailOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditModule" /> class.
    /// </summary>
    /// <param name="options">The hash chain configuration.</param>
    public MartenAuditModule(MartenAuditTrailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _options.Validate();

        if (_options.ChainConfiguration is not { } chain)
        {
            throw new LiteBusConfigurationException(
                "The Marten audit module requires the audit hash chain. Call MartenAuditTrailOptions.UseHashChain " +
                "when configuring UseMartenAuditChain.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(MartenAuditTrailOptions), _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditChain),
            serviceProvider => new MartenAuditChain(
                (IDocumentStore) (serviceProvider.GetService(typeof(IDocumentStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentStore))),
                (MartenAuditTrailOptions?) serviceProvider.GetService(typeof(MartenAuditTrailOptions)),
                (TimeProvider?) serviceProvider.GetService(typeof(TimeProvider)),
                serviceProvider),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICheckpointSource),
            serviceProvider => new MartenAuditCheckpointSource(
                (IDocumentStore) (serviceProvider.GetService(typeof(IDocumentStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentStore)))),
            InstanceLifetime.Singleton));

        if (!chain.CheckpointPolicy.IsTimeBased)
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(MartenAuditCheckpointBackgroundService),
            serviceProvider => new MartenAuditCheckpointBackgroundService(
                (IDocumentStore) (serviceProvider.GetService(typeof(IDocumentStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentStore))),
                (MartenAuditTrailOptions) (serviceProvider.GetService(typeof(MartenAuditTrailOptions)) ?? throw new LiteBusDependencyResolutionException(typeof(MartenAuditTrailOptions))),
                (TimeProvider?) serviceProvider.GetService(typeof(TimeProvider)),
                serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterBackgroundService(typeof(MartenAuditCheckpointBackgroundService));
    }
}
