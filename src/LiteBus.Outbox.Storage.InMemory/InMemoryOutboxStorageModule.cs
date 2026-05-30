using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

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

        var store = new InMemoryOutboxStore();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxLeaseStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStateStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InMemoryOutboxStore),
            store));
    }
}
