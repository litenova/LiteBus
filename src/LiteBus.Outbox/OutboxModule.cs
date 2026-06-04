using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Module for configuring durable outbox orchestration.
/// </summary>
public sealed class OutboxModule : ICompositeModule
{
    /// <summary>
    ///     The module builder callback invoked during <see cref="DeclareChildren" />.
    /// </summary>
    private readonly Action<OutboxModuleBuilder> _configure;

    /// <summary>
    ///     The builder populated when child modules are declared.
    /// </summary>
    private OutboxModuleBuilder? _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxModule" /> class.
    /// </summary>
    /// <param name="configure">The module configuration action.</param>
    public OutboxModule(Action<OutboxModuleBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        _builder = new OutboxModuleBuilder();
        _configure(_builder);

        foreach (var subModule in _builder.CollectSubModules())
        {
            registerChild(subModule);
        }
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (_builder is null)
        {
            throw new InvalidOperationException(
                "OutboxModule.Build was called without a prior DeclareChildren call. " +
                "Register the module through IModuleRegistry.");
        }

        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        _builder.ApplyContracts(contractRegistry);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            contractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IContractReader),
            contractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxProcessorOptions),
            _builder.ProcessorOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutbox),
            typeof(Outbox)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessor),
            typeof(OutboxProcessor)));

        if (_builder.IsOutboxProcessorEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxProcessorHostOptions),
                _builder.ProcessorHostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxProcessorBackgroundService),
                typeof(OutboxProcessorBackgroundService)));

            configuration.RegisterBackgroundService(typeof(OutboxProcessorBackgroundService));
        }

        if (_builder.IsCleanupEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxCleanupHostOptions),
                _builder.CleanupHostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxCleanupBackgroundService),
                typeof(OutboxCleanupBackgroundService)));

            configuration.RegisterBackgroundService(typeof(OutboxCleanupBackgroundService));
        }

        configuration.SetContext(new OutboxCoreRegisteredMarker());
    }
}
