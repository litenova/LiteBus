using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox;

/// <summary>
///     Module for configuring durable outbox orchestration.
/// </summary>
public sealed class OutboxModule : ICompositeModule, IRequires<MessageModule>
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
            if (subModule is not IModule module)
            {
                throw new LiteBusConfigurationException(
                    $"Outbox sub-module '{subModule.GetType().FullName}' must implement {nameof(IModule)}.");
            }

            registerChild(module);
        }
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (_builder is null)
        {
            throw new LiteBusConfigurationException(
                "OutboxModule.Build was called without a prior DeclareChildren call. " +
                "Register the module through IModuleRegistry.");
        }

        if (_builder.IsOutboxProcessorEnabled &&
            (!_builder.IsStorageConfigured || !_builder.IsDispatcherConfigured))
        {
            throw new LiteBusConfigurationException(
                "EnableOutboxProcessor requires both storage and dispatcher to be configured. " +
                "Call UseInMemoryStorage, UsePostgreSqlStorage, or UseEfCoreStorage and " +
                "UseInProcessDispatcher or UseAmqpDispatcher inside AddOutboxModule(...).");
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
            typeof(IOutboxManager),
            typeof(OutboxManager)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessor),
            CreateOutboxProcessor,
            InstanceLifetime.Transient));

        if (_builder.IsOutboxProcessorEnabled)
        {
            var processorControl = new OutboxProcessorControl();

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxProcessorControl),
                processorControl));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IOutboxProcessorControl),
                processorControl));

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

    /// <summary>
    ///     Creates an <see cref="OutboxProcessor" /> from the dependency injection container.
    /// </summary>
    /// <param name="services">The service provider used to resolve processor dependencies.</param>
    /// <returns>The configured outbox processor instance.</returns>
    private static object CreateOutboxProcessor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetService(typeof(ILogger<OutboxProcessor>)) as ILogger<OutboxProcessor>
                     ?? NullLogger<OutboxProcessor>.Instance;

        return new OutboxProcessor(
            (IOutboxLeaseStore)services.GetService(typeof(IOutboxLeaseStore))!,
            (IOutboxStateWriter)services.GetService(typeof(IOutboxStateWriter))!,
            (IOutboxDispatcher)services.GetService(typeof(IOutboxDispatcher))!,
            (OutboxProcessorOptions)services.GetService(typeof(OutboxProcessorOptions))!,
            services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System,
            logger);
    }
}
