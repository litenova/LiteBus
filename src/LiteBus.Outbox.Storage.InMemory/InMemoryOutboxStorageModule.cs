using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Registers the in-memory outbox store with the LiteBus module pipeline.
/// </summary>
public sealed class InMemoryOutboxStorageModule : IModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<OutboxCoreRegisteredMarker>(out _))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(InMemoryOutboxStorageModule)} requires OutboxModule core services " +
                "to be registered first. Configure storage inside AddOutboxModule(...) " +
                "using UseInMemoryStorage().");
        }

        var store = new InMemoryOutboxStore();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxLeaseStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStateWriter),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDeadLetterStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxRetentionStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDiagnosticsStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxMessageQuery),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxPurgeStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InMemoryOutboxStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxWorkSignal),
            typeof(OutboxPollingWorkSignal)));
    }
}
