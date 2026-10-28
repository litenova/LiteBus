using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
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
        ArgumentNullException.ThrowIfNull(configure);

        _configure = configure;
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
            throw new DurableStorageConfigurationException(
                "OutboxModule.Build was called without a prior DeclareChildren call. " +
                "Register the module through IModuleRegistry.");
        }

        if (!_builder.IsStorageConfigured)
        {
            throw new DurableStorageConfigurationException(
                "Outbox storage is required because AddOutboxModule registers IOutbox and related writer services. " +
                "Call UseInMemoryStorage, UsePostgreSqlStorage, or UseEntityFrameworkCoreStorage inside AddOutboxModule(...).");
        }

        if (_builder.IsOutboxProcessorEnabled && !_builder.IsDispatcherConfigured)
        {
            throw new DurableStorageConfigurationException(
                "EnableOutboxProcessor requires an outbox dispatcher. " +
                "Call UseInProcessDispatch, a broker-specific Use*Dispatch extension, or RegisterDispatcher inside AddOutboxModule(...).");
        }

        if (_builder.IsPerHandlerDeliveryEnabled &&
            !_builder.IsPerHandlerDeliveryWithTransportDispatchAllowed &&
            _builder.DispatcherModule is { PublishesFullEventPerEnvelope: true })
        {
            throw new DurableStorageConfigurationException(
                "EnablePerHandlerDelivery writes one envelope per subscribed handler, but the configured transport " +
                "dispatcher publishes the full event once per envelope, so each event would be published once per " +
                "handler. Use in-process dispatch with per-handler delivery, or call " +
                "AllowPerHandlerDeliveryWithTransportDispatch when the duplicate broker publication is intended.");
        }

        if (_builder.IsOutboxProcessorEnabled)
        {
            _builder.ProcessorHostOptions.Validate();
            _builder.ProcessorOptions.Retry.Validate();
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
            typeof(OutboxProcessorOptions),
            _builder.ProcessorOptions));

        if (_builder.CollectPayloadEncryptor() is { } outboxEncryptor)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IOutboxPayloadProtector),
                new OutboxPayloadProtector(outboxEncryptor)));
        }

        if (_builder.CollectContractResolver() is { } contractResolver)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IMessageContractResolver),
                contractResolver));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxEnqueueOptions),
            _builder.CollectEnqueueOptions()));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxEnvelopeFactory),
            typeof(OutboxEnvelopeFactory)));

        if (_builder.IsCollectedEventStagingEnabled)
        {
            RegisterCollectedEventStaging(configuration);
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutbox),
            typeof(Outbox)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxCleanupHostOptions),
            _builder.CleanupHostOptions));

        var retentionCoordinator = new OutboxRetentionCoordinator(_builder.CleanupHostOptions);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxRetentionCoordinator),
            retentionCoordinator));

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
                typeof(OutboxCleanupBackgroundService),
                typeof(OutboxCleanupBackgroundService)));

            configuration.RegisterBackgroundService(typeof(OutboxCleanupBackgroundService));
        }

        RegisterObservableMetrics(configuration);
        RegisterDiagnosticChecks(configuration);

    }

    /// <summary>
    ///     Registers the default scoped domain event collector and the open generic staging completion handler.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the staging registrations.</param>
    /// <remarks>
    ///     The handler is registered as an open generic definition, so the registry closes it over every message type
    ///     known now and every message type registered by modules that build later. The open generic definition is
    ///     registered in the container for the same reason: closed handler types are resolved through it regardless of
    ///     module build order.
    /// </remarks>
    private static void RegisterCollectedEventStaging(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IDomainEventCollector),
            typeof(DomainEventCollector),
            InstanceLifetime.Scoped));

        var messageRegistry = configuration.GetContext<IMessageRegistry>();
        messageRegistry.Register(typeof(StageCollectedDomainEventsCompletionHandler<>));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(StageCollectedDomainEventsCompletionHandler<>),
            typeof(StageCollectedDomainEventsCompletionHandler<>),
            InstanceLifetime.Scoped));
    }

    /// <summary>
    ///     Registers outbox observable OpenTelemetry gauges when they have not already been configured.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the metrics registration.</param>
    private static void RegisterObservableMetrics(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxObservableMetrics),
            static serviceProvider => new OutboxObservableMetrics(serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterStartupTask(typeof(OutboxObservableMetricsInitializer));
    }

    /// <summary>
    ///     Registers consumer-owned diagnostic probes collected by the outbox module builder.
    /// </summary>
    /// <param name="configuration">The module configuration receiving probe registrations.</param>
    /// <remarks>
    ///     Only the manifest entry is recorded. The host adapter called from AddLiteBus registers every manifest
    ///     probe in the container once, so registering it here as well would add a second descriptor.
    /// </remarks>
    private void RegisterDiagnosticChecks(IModuleConfiguration configuration)
    {
        foreach (var registration in _builder!.CollectDiagnosticChecks())
        {
            configuration.RegisterDiagnosticCheck(registration.ImplementationType, registration.Name);
        }
    }

    /// <summary>
    ///     Creates an <see cref="IOutboxProcessor" /> from the dependency injection container.
    /// </summary>
    /// <param name="services">The service provider used to resolve processor dependencies.</param>
    /// <returns>The configured outbox processor instance.</returns>
    private static object CreateOutboxProcessor(IServiceProvider services)
    {
        return OutboxProcessorFactory.Create(services);
    }
}
