using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Configures the outbox module and collects its sub-modules.
///     Extension methods for specific backends call
///     <see cref="RegisterStorage" /> or <see cref="RegisterDispatcher" /> on this builder.
/// </summary>
public sealed class OutboxModuleBuilder
{
    /// <summary>
    ///     Deferred contract registrations applied when the outbox module builds.
    /// </summary>
    private readonly MessageContractBuilder _contracts = new();

    /// <summary>
    ///     Consumer-owned diagnostic probes registered for this outbox.
    /// </summary>
    private readonly List<DiagnosticCheckRegistration> _diagnosticChecks = [];

    /// <summary>
    ///     The configured dispatcher sub-module, if any.
    /// </summary>
    private IOutboxDispatcherModule? _dispatcherModule;

    /// <summary>
    ///     The processor options supplied by the application or the framework defaults.
    /// </summary>
    private OutboxProcessorOptions _processorOptions = new();

    /// <summary>
    ///     The contract resolver registered through <see cref="UseContractResolver" />, if any.
    /// </summary>
    private IMessageContractResolver? _contractResolver;

    /// <summary>
    ///     Whether <see cref="EnableCleanup" /> was called.
    /// </summary>
    private bool _enableCleanup;

    /// <summary>
    ///     Whether <see cref="EnableOutboxProcessor" /> was called.
    /// </summary>
    private bool _enableOutboxProcessor;

    /// <summary>
    ///     The optional payload encryptor registered through <see cref="UsePayloadEncryption" />.
    /// </summary>
    private IPayloadEncryptor? _payloadEncryptor;

    /// <summary>
    ///     The configured storage sub-module, if any.
    /// </summary>
    private IOutboxStorageModule? _storageModule;

    /// <summary>
    ///     Whether <see cref="StageCollectedEvents" /> was called.
    /// </summary>
    private bool _stageCollectedEvents;

    /// <summary>
    ///     Whether <see cref="UseAmbientTrace" /> was called.
    /// </summary>
    private bool _useAmbientTrace;

    /// <summary>
    ///     Whether <see cref="EnablePerHandlerDelivery" /> was called.
    /// </summary>
    private bool _perHandlerDelivery;

    /// <summary>
    ///     Whether <see cref="AllowPerHandlerDeliveryWithTransportDispatch" /> was called.
    /// </summary>
    private bool _allowPerHandlerDeliveryWithTransportDispatch;

    /// <summary>
    ///     Gets the deferred contract writer. Registrations are applied to the shared
    ///     <see cref="IMessageContractRegistry" /> when the module builds.
    /// </summary>
    public IContractWriter Contracts => _contracts;

    /// <summary>
    ///     Gets the options controlling processor batch size, lease duration, and retry.
    /// </summary>
    public OutboxProcessorOptions ProcessorOptions => _processorOptions.HookFailurePolicy.HasValue || _dispatcherModule is null
        ? _processorOptions
        : _processorOptions with
        {
            HookFailurePolicy = _dispatcherModule.DefaultHookFailurePolicy
        };

    /// <summary>
    ///     Gets the options controlling the background service lifecycle and poll behaviour.
    /// </summary>
    public OutboxProcessorHostOptions ProcessorHostOptions { get; } = new();

    /// <summary>
    ///     Gets the options for the optional outbox retention cleanup loop.
    /// </summary>
    public OutboxCleanupHostOptions CleanupHostOptions { get; } = new();

    /// <summary>
    ///     Gets a value indicating whether the outbox processor background service is registered.
    /// </summary>
    public bool IsOutboxProcessorEnabled => _enableOutboxProcessor;

    /// <summary>
    ///     Gets a value indicating whether the outbox cleanup background service is registered.
    /// </summary>
    public bool IsCleanupEnabled => _enableCleanup;

    /// <summary>
    ///     Gets a value indicating whether a storage sub-module was registered on this builder.
    /// </summary>
    public bool IsStorageConfigured => _storageModule is not null;

    /// <summary>
    ///     Gets a value indicating whether a dispatcher sub-module was registered on this builder.
    /// </summary>
    public bool IsDispatcherConfigured => _dispatcherModule is not null;

    /// <summary>
    ///     Activates the background processor that polls for and dispatches
    ///     pending outbox messages.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder EnableOutboxProcessor(Action<OutboxProcessorHostOptions>? configure = null)
    {
        _enableOutboxProcessor = true;
        configure?.Invoke(ProcessorHostOptions);
        return this;
    }

    /// <summary>
    ///     Registers the outbox retention cleanup background loop for the generic host.
    /// </summary>
    /// <param name="configure">An optional callback that configures cleanup interval and retention.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder EnableCleanup(Action<OutboxCleanupHostOptions>? configure = null)
    {
        _enableCleanup = true;
        configure?.Invoke(CleanupHostOptions);
        return this;
    }

    /// <summary>
    ///     Replaces the default processor options.
    /// </summary>
    /// <param name="options">The batch, lease, owner, and retry options used by the processor.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseProcessorOptions(OutboxProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _processorOptions = options;

        return this;
    }

    /// <summary>
    ///     Registers the contract resolver used to select the CLR type for contract lookup during enqueue.
    /// </summary>
    /// <param name="resolver">The resolver that overrides contract lookup type selection.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseContractResolver(IMessageContractResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _contractResolver = resolver;
        return this;
    }

    /// <summary>
    ///     Collects the contract resolver registered on this builder, if any.
    /// </summary>
    /// <returns>The registered resolver, or <see langword="null" /> when none was registered.</returns>
    internal IMessageContractResolver? CollectContractResolver()
    {
        return _contractResolver;
    }

    /// <summary>
    ///     Registers the storage sub-module. Exactly one storage module must be
    ///     registered. Called by extension methods such as UsePostgreSqlStorage().
    /// </summary>
    /// <param name="storageModule">The storage module to register as a child of the outbox module.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder RegisterStorage(IOutboxStorageModule storageModule)
    {
        ArgumentNullException.ThrowIfNull(storageModule);

        if (_storageModule is not null)
        {
            throw new DurableStorageConfigurationException(
                "Outbox storage is already configured. " +
                "Call only one of UsePostgreSqlStorage, UseEntityFrameworkCoreStorage, " +
                "or UseInMemoryStorage.");
        }

        _storageModule = storageModule;
        return this;
    }

    /// <summary>
    ///     Registers the dispatcher sub-module. Exactly one dispatcher must be
    ///     registered. Called by extension methods such as UseInProcessDispatch().
    /// </summary>
    /// <param name="dispatcherModule">The dispatcher module to register as a child of the outbox module.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <see cref="ProcessorOptions" /> keeps an explicit <see cref="ProcessorOptions.HookFailurePolicy" /> value and
    ///     otherwise resolves the dispatcher recommendation after configuration, independent of registration order. A
    ///     caller-set value wins. Leaving the policy unset keeps transport dispatchers on
    ///     <see cref="ProcessorHookFailurePolicy.CompleteDespiteHookFailure" />; overriding it with
    ///     <see cref="ProcessorHookFailurePolicy.DeadLetter" /> dead-letters a message the broker already accepted,
    ///     so the message is re-leased after replay and published a second time.
    /// </remarks>
    public OutboxModuleBuilder RegisterDispatcher(IOutboxDispatcherModule dispatcherModule)
    {
        ArgumentNullException.ThrowIfNull(dispatcherModule);

        if (_dispatcherModule is not null)
        {
            throw new DurableStorageConfigurationException(
                "Outbox dispatcher is already configured. " +
                "Call only one outbox dispatcher registration method such as UseInProcessDispatch or a broker-specific Use*Dispatch extension.");
        }

        _dispatcherModule = dispatcherModule;
        return this;
    }

    /// <summary>
    ///     Registers payload encryption for outbox acceptance and dispatch.
    /// </summary>
    /// <param name="encryptor">The encryptor used before store writes and after store reads.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UsePayloadEncryption(IPayloadEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(encryptor);

        _payloadEncryptor = encryptor;
        return this;
    }

    /// <summary>
    ///     Collects the configured payload encryptor, if any.
    /// </summary>
    /// <returns>The encryptor registered on this builder.</returns>
    internal IPayloadEncryptor? CollectPayloadEncryptor()
    {
        return _payloadEncryptor;
    }

    /// <summary>
    ///     Registers the default scoped domain event collector and stages collected events into the transactional outbox when a mediation completes successfully.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Handlers record consequences through <see cref="IDomainEventCollector" /> instead of publishing them
    ///         inline. A completion handler at <see cref="HandlerPriorities.Persistence" /> drains the collector into
    ///         <see cref="ITransactionalOutbox" /> on <see cref="MediationOutcome.Succeeded" /> only, ordered before an
    ///         application's commit at <see cref="HandlerPriorities.UnitOfWork" />, so the staged rows join the unit of
    ///         work that produced them. A failed mediation stages nothing.
    ///     </para>
    ///     <para>
    ///         The application must register a scoped <see cref="ITransactionalOutbox" />, including the Entity Framework
    ///         Core interceptor non-generic adapter over <see cref="ITransactionalOutbox{TContext}" />; the staging handler
    ///         resolves it lazily and only when the drain is non-empty, so mediations that collect nothing never touch it.
    ///         When <see cref="UseAmbientTrace" /> is also enabled, staged envelopes stamp
    ///         <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.FromAmbient" />.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder StageCollectedEvents()
    {
        _stageCollectedEvents = true;
        return this;
    }

    /// <summary>
    ///     Stamps trace metadata from the ambient execution context onto enqueued envelopes whose items carry none.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         When an item's trace is
    ///         <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.None" />, the envelope factory
    ///         stores <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.FromAmbient" /> instead.
    ///         An explicit item trace always wins over the ambient value, and outside a mediation the ambient read
    ///         yields none, so the envelope is identical to one enqueued without this option.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder UseAmbientTrace()
    {
        _useAmbientTrace = true;
        return this;
    }

    /// <summary>
    ///     Writes one envelope per subscribed main handler of each enqueued event, so every reaction gets its own retry, dead letter, and replay.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Each envelope records the full name of the handler it is addressed to, and in-process dispatch publishes
    ///         each envelope to that handler alone. Enqueueing an event with no subscribed main handler throws
    ///         <see cref="NoHandlerFoundException" />, matching the default in-process dispatch rule that treats an
    ///         unhandled durable message as a failure.
    ///     </para>
    ///     <para>
    ///         The trade-off is storage: one row per handler instead of one row per event. A handler added after enqueue
    ///         does not receive already-enqueued events, and a handler removed before dispatch leaves its envelopes with
    ///         no matching subscriber, which the dispatch <c>RequireHandler</c> option then decides. Use this only with
    ///         in-process dispatch: broker dispatchers publish the full event per envelope, so per-handler envelopes
    ///         would duplicate broker publication. Composition throws <see cref="DurableStorageConfigurationException" />
    ///         when per-handler delivery meets a transport dispatcher unless
    ///         <see cref="AllowPerHandlerDeliveryWithTransportDispatch" /> acknowledges the duplication.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder EnablePerHandlerDelivery()
    {
        _perHandlerDelivery = true;
        return this;
    }

    /// <summary>
    ///     Acknowledges that per-handler envelopes duplicate broker publication when combined with a transport dispatcher.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Call this only when the duplicate publication is intended, for example during a migration that replays
    ///     per-handler rows through a broker. Without this acknowledgement, composition throws
    ///     <see cref="DurableStorageConfigurationException" /> when <see cref="EnablePerHandlerDelivery" /> meets a
    ///     transport dispatcher.
    /// </remarks>
    public OutboxModuleBuilder AllowPerHandlerDeliveryWithTransportDispatch()
    {
        _allowPerHandlerDeliveryWithTransportDispatch = true;
        return this;
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="StageCollectedEvents" /> was called.
    /// </summary>
    internal bool IsCollectedEventStagingEnabled => _stageCollectedEvents;

    /// <summary>
    ///     Gets a value indicating whether <see cref="AllowPerHandlerDeliveryWithTransportDispatch" /> was called.
    /// </summary>
    internal bool IsPerHandlerDeliveryWithTransportDispatchAllowed => _allowPerHandlerDeliveryWithTransportDispatch;

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnablePerHandlerDelivery" /> was called.
    /// </summary>
    internal bool IsPerHandlerDeliveryEnabled => _perHandlerDelivery;

    /// <summary>
    ///     Gets the dispatcher sub-module registered on this builder, if any.
    /// </summary>
    internal IOutboxDispatcherModule? DispatcherModule => _dispatcherModule;

    /// <summary>
    ///     Collects the enqueue options built from the ambient trace and per-handler delivery flags.
    /// </summary>
    /// <returns>The enqueue options the module registers.</returns>
    internal OutboxEnqueueOptions CollectEnqueueOptions()
    {
        return new OutboxEnqueueOptions
        {
            UseAmbientTrace = _useAmbientTrace,
            PerHandlerDelivery = _perHandlerDelivery
        };
    }

    /// <summary>
    ///     Registers a consumer-owned diagnostic probe for this outbox.
    /// </summary>
    /// <typeparam name="TCheck">The probe type that implements <see cref="IDiagnosticCheck" />.</typeparam>
    /// <param name="name">The probe name reported to operators and health hosts.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder AddDiagnosticCheck<TCheck>(string name)
        where TCheck : IDiagnosticCheck
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _diagnosticChecks.Add(new DiagnosticCheckRegistration(typeof(TCheck), name));
        return this;
    }

    /// <summary>
    ///     Collects consumer-owned diagnostic probes registered on this builder.
    /// </summary>
    /// <returns>The diagnostic probe registrations declared on this builder.</returns>
    internal IReadOnlyList<DiagnosticCheckRegistration> CollectDiagnosticChecks()
    {
        return _diagnosticChecks;
    }

    /// <summary>
    ///     Collects configured sub-modules in storage then dispatcher order.
    /// </summary>
    /// <returns>The sub-modules declared on this builder.</returns>
    public IReadOnlyList<IModule> CollectSubModules()
    {
        return (_storageModule, _dispatcherModule) switch
        {
            (not null, not null) => [_storageModule, _dispatcherModule],
            (not null, null) => [_storageModule],
            (null, not null) => [_dispatcherModule],
            _ => []
        };
    }

    /// <summary>
    ///     Applies deferred contract registrations to the live registry.
    /// </summary>
    /// <param name="registry">The shared message contract registry.</param>
    public void ApplyContracts(IMessageContractRegistry registry)
    {
        _contracts.ApplyTo(registry);
    }
}
