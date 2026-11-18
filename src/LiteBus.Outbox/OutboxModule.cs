using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.ImmediateDispatch;
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

        if (_builder.IsPerHandlerDeliveryEnabled)
        {
            var messageRegistry = configuration.GetContext<IMessageRegistry>();

            configuration.RegisterCompositionValidation(() => ValidateSubscriberNames(messageRegistry));
        }

        if (_builder.ImmediateDispatchOptions is not null && !_builder.IsPerHandlerDeliveryEnabled)
        {
            var messageRegistry = configuration.GetContext<IMessageRegistry>();

            configuration.RegisterCompositionValidation(() => ValidateImmediateDispatchHandlerAttributes(messageRegistry));
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

        if (_builder.ImmediateDispatchOptions is { } immediateDispatch)
        {
            RegisterImmediateDispatch(configuration, immediateDispatch);
        }

        RegisterObservableMetrics(configuration);
        RegisterDiagnosticChecks(configuration);
    }

    /// <summary>
    ///     Registers the services immediate dispatch needs and the check that its requirements are met.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    /// <param name="options">The immediate dispatch options from the builder.</param>
    /// <remarks>
    ///     The unit of work, the dispatcher and the store are registered by other modules or child modules that may
    ///     build after this one, so the requirements are checked as a composition validation once every module has
    ///     built.
    /// </remarks>
    private static void RegisterImmediateDispatch(IModuleConfiguration configuration, OutboxImmediateDispatchOptions options)
    {
        options.Validate();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxImmediateDispatchOptions),
            options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(StagedImmediateEnvelopes),
            typeof(StagedImmediateEnvelopes),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
            typeof(IUnitOfWorkCommitListener),
            typeof(OutboxImmediateDispatchListener),
            InstanceLifetime.Scoped));

        configuration.RegisterCompositionValidation(() => ValidateImmediateDispatch(configuration.DependencyRegistry));
    }

    /// <summary>
    ///     Throws when a service immediate dispatch needs is not registered.
    /// </summary>
    /// <param name="registry">The completed dependency registry.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     No unit of work, no outbox dispatcher, or no store implementing <see cref="IOutboxTargetedLeaseStore" /> is
    ///     registered.
    /// </exception>
    private static void ValidateImmediateDispatch(IDependencyRegistry registry)
    {
        var missing = new List<string>();

        if (!registry.Any(static descriptor => descriptor.DependencyType == typeof(IUnitOfWork)))
        {
            missing.Add("a unit of work (call UseUnitOfWork on the command module), because dispatch runs after its commit");
        }

        if (!registry.Any(static descriptor => descriptor.DependencyType == typeof(IOutboxDispatcher)))
        {
            missing.Add("an outbox dispatcher (call UseInProcessDispatch or a broker Use*Dispatch extension)");
        }

        if (!registry.Any(static descriptor => descriptor.DependencyType == typeof(IOutboxTargetedLeaseStore)))
        {
            missing.Add(
                "an outbox store that implements IOutboxTargetedLeaseStore (the shipped PostgreSQL, Entity Framework "
                + "Core and in-memory stores do)");
        }

        if (missing.Count > 0)
        {
            throw new LiteBusConfigurationException(
                "EnableImmediateDispatch requires " + string.Join("; ", missing) + ".");
        }
    }

    /// <summary>
    ///     Validates that no registered main handler carries <see cref="DispatchImmediatelyAttribute" /> when immediate
    ///     dispatch is enabled without per-handler delivery.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     A registered handler class carries <see cref="DispatchImmediatelyAttribute" /> while per-handler delivery is off.
    /// </exception>
    /// <remarks>
    ///     Runs as a composition validation rather than during <see cref="Build" /> because the modules that
    ///     register event handlers build after the outbox module, so their handlers are not visible yet while this
    ///     module builds. Direct and indirect main handlers are validated together because per-handler delivery writes
    ///     one envelope per handler in both sets, and handler types are deduplicated because one handler class may
    ///     observe several messages.
    /// </remarks>
    private static void ValidateImmediateDispatchHandlerAttributes(IMessageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var attributed = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in reader)
        {
            var handlerTypes = descriptor.Handlers.Select(static handler => handler.HandlerType)
                .Concat(descriptor.IndirectHandlers.Select(static handler => handler.HandlerType));

            foreach (var handlerType in handlerTypes)
            {
                if (handlerType.GetCustomAttribute<DispatchImmediatelyAttribute>(inherit: false) is not null)
                {
                    attributed.Add(handlerType.FullName ?? handlerType.Name);
                }
            }
        }

        if (attributed.Count == 0)
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "EnableImmediateDispatch is set without per-handler delivery, but the following handler classes carry " +
            "[DispatchImmediately], which is ignored without per-handler envelopes: " +
            string.Join(", ", attributed.Select(static name => $"'{name}'")) +
            ". Call EnablePerHandlerDelivery() inside AddOutboxModule(...) so each handler gets its own envelope, " +
            "or move [DispatchImmediately] to the event type for event-level immediacy.");
    }

    /// <summary>
    ///     Validates the subscriber names declared on main handlers once every module has registered its handlers.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     A subscriber name is invalid, two main handlers of one message share a name, or a subscriber name equals
    ///     another handler's full type name.
    /// </exception>
    /// <remarks>
    ///     Runs as a composition validation rather than during <see cref="Build" /> because the modules that
    ///     register event handlers build after the outbox module, so their handlers are not visible yet while this
    ///     module builds. Every violation is reported in one failure, so adopting subscriber names across many
    ///     handlers does not take one composition run per mistake.
    /// </remarks>
    private static void ValidateSubscriberNames(IMessageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        List<string> violations = [];

        foreach (var descriptor in reader)
        {
            ValidateDescriptorSubscriberNames(descriptor, violations);
        }

        if (violations.Count == 0)
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "Per-handler outbox delivery found invalid subscriber names:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations) +
            Environment.NewLine +
            $"Give every main handler of one message a distinct subscriber name of at most {SubscriberNames.MaxLength} " +
            "characters using only letters, digits, '.', '-' and '_', and keep subscriber names distinct from the " +
            "full type names of the other handlers of the same message.");
    }

    /// <summary>
    ///     Validates the subscriber names of the main handlers subscribed to one message.
    /// </summary>
    /// <param name="descriptor">The message descriptor holding the subscribed main handlers.</param>
    /// <param name="violations">The violation lines collected across every message descriptor.</param>
    /// <remarks>
    ///     Direct and indirect main handlers are validated together because per-handler delivery writes one envelope
    ///     per handler in both sets. Handler types are deduplicated first because one handler class may implement
    ///     several contracts for the same message, which must not read as a handler colliding with itself.
    /// </remarks>
    private static void ValidateDescriptorSubscriberNames(IMessageDescriptor descriptor, List<string> violations)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(violations);

        var messageName = descriptor.MessageType.FullName ?? descriptor.MessageType.Name;
        var handlerTypes = descriptor.Handlers.Select(static handler => handler.HandlerType)
            .Concat(descriptor.IndirectHandlers.Select(static handler => handler.HandlerType))
            .Distinct()
            .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            try
            {
                SubscriberNames.ThrowIfInvalid(handlerType);
            }
            catch (LiteBusConfigurationException exception)
            {
                violations.Add($"  {exception.Message}");
            }
        }

        var ownersByName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);

        foreach (var handlerType in handlerTypes)
        {
            var name = SubscriberNames.Resolve(handlerType);

            if (!ownersByName.TryGetValue(name, out var owners))
            {
                owners = [];
                ownersByName[name] = owners;
            }

            owners.Add(handlerType);
        }

        foreach (var entry in ownersByName.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (entry.Value.Count < 2)
            {
                continue;
            }

            violations.Add(
                $"  Message '{messageName}' has {entry.Value.Count} main handlers with subscriber name '{entry.Key}': " +
                string.Join(", ", entry.Value.Select(static type => $"'{type.FullName ?? type.Name}'")) + ".");
        }

        var typesByFullName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var handlerType in handlerTypes)
        {
            typesByFullName.TryAdd(handlerType.FullName ?? handlerType.Name, handlerType);
        }

        foreach (var handlerType in handlerTypes)
        {
            var name = SubscriberNames.Resolve(handlerType);

            if (typesByFullName.TryGetValue(name, out var owner) && owner != handlerType)
            {
                violations.Add(
                    $"  Subscriber name '{name}' on handler '{handlerType.FullName ?? handlerType.Name}' equals the " +
                    $"full type name of handler '{owner.FullName ?? owner.Name}' for message '{messageName}'.");
            }
        }
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
