using System;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Registers the in-memory outbox store with the LiteBus module pipeline.
/// </summary>
public sealed class InMemoryOutboxStorageModule : IOutboxStorageModule, IRequires<OutboxModule>
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<InMemoryOutboxStorageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryOutboxStorageModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public InMemoryOutboxStorageModule(Action<InMemoryOutboxStorageModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new InMemoryOutboxStorageModuleBuilder();
        _builder(moduleBuilder);

        var timeProvider = moduleBuilder.TimeProvider ?? TimeProvider.System;
        var store = new InMemoryOutboxStore(moduleBuilder.Options, timeProvider);

        if (moduleBuilder.TimeProvider is not null)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(TimeProvider), timeProvider));
        }

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
            typeof(IOutboxProcessingStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxOperationsStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InMemoryOutboxStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxWorkSignal),
            typeof(OutboxPollingWorkSignal)));
    }
}
