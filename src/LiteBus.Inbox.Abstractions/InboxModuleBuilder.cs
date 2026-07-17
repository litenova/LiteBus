using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Configures the inbox module and collects its sub-modules.
///     Extension methods for specific backends call
///     <see cref="RegisterStorage" />, <see cref="RegisterDispatcher" />,
///     or <see cref="RegisterIngress" /> on this builder.
/// </summary>
public sealed class InboxModuleBuilder
{
    /// <summary>
    ///     Deferred contract registrations applied when the inbox module builds.
    /// </summary>
    private readonly MessageContractBuilder _contracts = new();

    /// <summary>
    ///     Consumer-owned diagnostic probes registered for this inbox.
    /// </summary>
    private readonly List<DiagnosticCheckRegistration> _diagnosticChecks = [];

    /// <summary>
    ///     The configured ingress sub-module, if any.
    /// </summary>
    private IInboxIngressModule? _ingressModule;

    /// <summary>
    ///     Saga sub-modules registered for this inbox.
    /// </summary>
    private readonly List<IModule> _sagaModules = [];

    /// <summary>
    ///     The configured dispatcher sub-module, if any.
    /// </summary>
    private IInboxDispatcherModule? _dispatcherModule;

    /// <summary>
    ///     Whether <see cref="EnableCleanup" /> was called.
    /// </summary>
    private bool _enableCleanup;

    /// <summary>
    ///     Whether <see cref="EnableInboxProcessor" /> was called.
    /// </summary>
    private bool _enableInboxProcessor;

    /// <summary>
    ///     The optional payload encryptor registered through <see cref="UsePayloadEncryption" />.
    /// </summary>
    private IPayloadEncryptor? _payloadEncryptor;

    /// <summary>
    ///     The configured storage sub-module, if any.
    /// </summary>
    private IInboxStorageModule? _storageModule;

    /// <summary>
    ///     Gets the deferred contract writer. Registrations are applied to the shared
    ///     <see cref="IMessageContractRegistry" /> when the module builds.
    /// </summary>
    public IContractWriter Contracts => _contracts;

    /// <summary>
    ///     Gets the options controlling processor batch size, lease duration, and retry.
    /// </summary>
    public InboxProcessorOptions ProcessorOptions { get; private set; } = new();

    /// <summary>
    ///     Gets the options controlling the background service lifecycle and poll behaviour.
    /// </summary>
    public InboxProcessorHostOptions ProcessorHostOptions { get; } = new();

    /// <summary>
    ///     Gets the options for the optional inbox retention cleanup loop.
    /// </summary>
    public InboxCleanupHostOptions CleanupHostOptions { get; } = new();

    /// <summary>
    ///     Gets a value indicating whether the inbox processor background service is registered.
    /// </summary>
    public bool IsInboxProcessorEnabled => _enableInboxProcessor;

    /// <summary>
    ///     Gets a value indicating whether the inbox cleanup background service is registered.
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
    ///     Gets a value indicating whether payload encryption was configured on this builder.
    /// </summary>
    public bool IsPayloadEncryptionConfigured => _payloadEncryptor is not null;

    /// <summary>
    ///     Activates the background processor that polls for and dispatches
    ///     pending inbox messages.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder EnableInboxProcessor(Action<InboxProcessorHostOptions>? configure = null)
    {
        _enableInboxProcessor = true;
        configure?.Invoke(ProcessorHostOptions);
        return this;
    }

    /// <summary>
    ///     Registers the inbox retention cleanup background loop for the generic host.
    /// </summary>
    /// <param name="configure">An optional callback that configures cleanup interval and retention.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder EnableCleanup(Action<InboxCleanupHostOptions>? configure = null)
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
    public InboxModuleBuilder UseProcessorOptions(InboxProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ProcessorOptions = options;
        return this;
    }

    /// <summary>
    ///     Registers the storage sub-module. Exactly one storage module must be
    ///     registered. Called by extension methods such as UsePostgreSqlStorage().
    /// </summary>
    /// <param name="storageModule">The storage module to register as a child of the inbox module.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder RegisterStorage(IInboxStorageModule storageModule)
    {
        ArgumentNullException.ThrowIfNull(storageModule);

        if (_storageModule is not null)
        {
            throw new LiteBusConfigurationException(
                "Inbox storage is already configured. " +
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
    /// <param name="dispatcherModule">The dispatcher module to register as a child of the inbox module.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder RegisterDispatcher(IInboxDispatcherModule dispatcherModule)
    {
        ArgumentNullException.ThrowIfNull(dispatcherModule);

        if (_dispatcherModule is not null)
        {
            throw new LiteBusConfigurationException(
                "Inbox dispatcher is already configured. " +
                "Call only one inbox dispatcher registration method such as UseInProcessDispatch or a broker-specific Use*Dispatch extension.");
        }

        _dispatcherModule = dispatcherModule;
        return this;
    }

    /// <summary>
    ///     Registers the ingress sub-module. Exactly one ingress source may be registered for an inbox module because
    ///     ingress host options, transport consumer, and background-service ownership are singular per module.
    ///     Called by extension methods such as UseAmqpIngress().
    /// </summary>
    /// <param name="ingressModule">The ingress module to register as a child of the inbox module.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder RegisterIngress(IInboxIngressModule ingressModule)
    {
        ArgumentNullException.ThrowIfNull(ingressModule);

        if (_ingressModule is not null)
        {
            throw new LiteBusConfigurationException(
                "Inbox ingress is already configured. Register one ingress source per inbox module.");
        }

        _ingressModule = ingressModule;
        return this;
    }

    /// <summary>
    ///     Registers a saga sub-module. Called by extension methods such as <c>EnableSaga()</c>.
    /// </summary>
    /// <param name="sagaModule">The saga module to register as a child of the inbox module.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder RegisterSaga(IModule sagaModule)
    {
        ArgumentNullException.ThrowIfNull(sagaModule);
        _sagaModules.Add(sagaModule);
        return this;
    }

    /// <summary>
    ///     Registers payload encryption for inbox acceptance and dispatch.
    /// </summary>
    /// <param name="encryptor">The encryptor used before store writes and after store reads.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder UsePayloadEncryption(IPayloadEncryptor encryptor)
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
    ///     Registers a consumer-owned diagnostic probe for this inbox.
    /// </summary>
    /// <typeparam name="TCheck">The probe type that implements <see cref="IDiagnosticCheck" />.</typeparam>
    /// <param name="name">The probe name reported to operators and health hosts.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder AddDiagnosticCheck<TCheck>(string name)
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
    ///     Collects configured sub-modules in storage, dispatcher, then ingress order.
    /// </summary>
    /// <returns>The sub-modules declared on this builder.</returns>
    public IReadOnlyList<IModule> CollectSubModules()
    {
        List<IModule> modules = [];

        if (_storageModule is not null)
        {
            modules.Add(_storageModule);
        }

        if (_dispatcherModule is not null)
        {
            modules.Add(_dispatcherModule);
        }

        if (_ingressModule is not null)
        {
            modules.Add(_ingressModule);
        }
        modules.AddRange(_sagaModules.Where(static module => module is ISagaStoreModule));
        modules.AddRange(_sagaModules.Where(static module => module is not ISagaStoreModule));
        return modules;
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
