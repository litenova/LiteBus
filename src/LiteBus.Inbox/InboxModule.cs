using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Module for configuring inbox acceptance and processing orchestration.
/// </summary>
public sealed class InboxModule : ICompositeModule
{
    /// <summary>
    ///     The module builder callback invoked during <see cref="DeclareChildren" />.
    /// </summary>
    private readonly Action<InboxModuleBuilder> _configure;

    /// <summary>
    ///     The builder populated when child modules are declared.
    /// </summary>
    private InboxModuleBuilder? _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxModule" /> class.
    /// </summary>
    /// <param name="configure">The module configuration action.</param>
    public InboxModule(Action<InboxModuleBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        _builder = new InboxModuleBuilder();
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
                "InboxModule.Build was called without a prior DeclareChildren call. " +
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
            typeof(InboxProcessorOptions),
            _builder.ProcessorOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInbox),
            typeof(Inbox)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.IInboxProcessor),
            typeof(InboxProcessor)));

        if (_builder.IsInboxProcessorEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxProcessorHostOptions),
                _builder.ProcessorHostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxProcessorBackgroundService),
                typeof(InboxProcessorBackgroundService)));

            configuration.RegisterBackgroundService(typeof(InboxProcessorBackgroundService));
        }

        if (_builder.IsCleanupEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxCleanupHostOptions),
                _builder.CleanupHostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxCleanupBackgroundService),
                typeof(InboxCleanupBackgroundService)));

            configuration.RegisterBackgroundService(typeof(InboxCleanupBackgroundService));
        }

        configuration.SetContext(new InboxCoreRegisteredMarker());
    }
}
