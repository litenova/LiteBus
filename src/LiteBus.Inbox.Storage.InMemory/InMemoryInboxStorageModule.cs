using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Module for registering the in-memory inbox store.
/// </summary>
public sealed class InMemoryInboxStorageModule : IInboxStorageModule, IRequires<InboxModule>
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<InMemoryInboxStorageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryInboxStorageModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public InMemoryInboxStorageModule(Action<InMemoryInboxStorageModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new InMemoryInboxStorageModuleBuilder();
        _builder(moduleBuilder);

        var timeProvider = moduleBuilder.TimeProvider ?? TimeProvider.System;
        var store = new InMemoryInboxStore(moduleBuilder.Options, timeProvider);

        if (moduleBuilder.TimeProvider is not null)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(TimeProvider), timeProvider));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxLeaseStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxStateWriter), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxDeadLetterStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxRetentionStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxDiagnosticsStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxMessageQuery), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxPurgeStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxProcessingStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxOperationsStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(InMemoryInboxStore), store));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxWorkSignal), typeof(InboxPollingWorkSignal)));
    }
}