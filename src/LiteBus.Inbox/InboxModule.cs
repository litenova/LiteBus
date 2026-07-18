using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox;

/// <summary>
///     Module for configuring inbox acceptance and processing orchestration.
/// </summary>
public sealed class InboxModule : ICompositeModule, IRequires<MessageModule>
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
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
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
            throw new LiteBusConfigurationException(
                "InboxModule.Build was called without a prior DeclareChildren call. " +
                "Register the module through IModuleRegistry.");
        }

        if (!_builder.IsStorageConfigured)
        {
            throw new LiteBusConfigurationException(
                "Inbox storage is required because AddInboxModule registers IInbox and related writer services. " +
                "Call UseInMemoryStorage, UsePostgreSqlStorage, or UseEntityFrameworkCoreStorage inside AddInboxModule(...).");
        }

        if (_builder.IsInboxProcessorEnabled && !_builder.IsDispatcherConfigured)
        {
            throw new LiteBusConfigurationException(
                "EnableInboxProcessor requires an inbox dispatcher. " +
                "Call UseInProcessDispatch, a broker-specific Use*Dispatch extension, or RegisterDispatcher inside AddInboxModule(...).");
        }

        if (_builder.IsInboxProcessorEnabled)
        {
            _builder.ProcessorHostOptions.Validate();
        }

        if (_builder.IsCleanupEnabled)
        {
            _builder.CleanupHostOptions.Validate();
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

        if (_builder.CollectPayloadEncryptor() is { } inboxEncryptor)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IInboxPayloadProtector),
                new InboxPayloadProtector(inboxEncryptor)));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxEnvelopeFactory),
            typeof(InboxEnvelopeFactory)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInbox),
            typeof(Inbox)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InboxCleanupHostOptions),
            _builder.CleanupHostOptions));

        var retentionCoordinator = new InboxRetentionCoordinator(_builder.CleanupHostOptions);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InboxRetentionCoordinator),
            retentionCoordinator));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxManager),
            typeof(InboxManager)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxProcessor),
            CreateInboxProcessor,
            InstanceLifetime.Transient));

        if (_builder.IsInboxProcessorEnabled)
        {
            var processorControl = new InboxProcessorControl();

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxProcessorControl),
                processorControl));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IInboxProcessorControl),
                processorControl));

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
                typeof(InboxCleanupBackgroundService),
                typeof(InboxCleanupBackgroundService)));

            configuration.RegisterBackgroundService(typeof(InboxCleanupBackgroundService));
        }

        RegisterObservableMetrics(configuration);
        RegisterDiagnosticChecks(configuration);

    }

    /// <summary>
    ///     Registers inbox observable OpenTelemetry gauges when they have not already been configured.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the metrics registration.</param>
    private static void RegisterObservableMetrics(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InboxObservableMetrics),
            static serviceProvider => new InboxObservableMetrics(serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterStartupTask(typeof(InboxObservableMetricsInitializer));
    }

    /// <summary>
    ///     Registers consumer-owned diagnostic probes collected by the inbox module builder.
    /// </summary>
    /// <param name="configuration">The module configuration receiving probe registrations.</param>
    private void RegisterDiagnosticChecks(IModuleConfiguration configuration)
    {
        foreach (var registration in _builder!.CollectDiagnosticChecks())
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                registration.ImplementationType,
                registration.ImplementationType,
                InstanceLifetime.Singleton));

            configuration.RegisterDiagnosticCheck(registration.ImplementationType, registration.Name);
        }
    }

    /// <summary>
    ///     Creates an <see cref="Abstractions.IInboxProcessor" /> from the dependency injection container.
    /// </summary>
    /// <param name="services">The service provider used to resolve processor dependencies.</param>
    /// <returns>The configured inbox processor instance.</returns>
    private static object CreateInboxProcessor(IServiceProvider services)
    {
        return InboxProcessorFactory.Create(services);
    }
}
