using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
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
    ///     Whether the application supplied processor options.
    /// </summary>
    private bool _processorOptionsExplicitlySet;

    /// <summary>
    ///     The processor options supplied by the application or the framework defaults.
    /// </summary>
    private OutboxProcessorOptions _processorOptions = new();

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
    ///     Gets the deferred contract writer. Registrations are applied to the shared
    ///     <see cref="IMessageContractRegistry" /> when the module builds.
    /// </summary>
    public IContractWriter Contracts => _contracts;

    /// <summary>
    ///     Gets the options controlling processor batch size, lease duration, and retry.
    /// </summary>
    public OutboxProcessorOptions ProcessorOptions => _processorOptionsExplicitlySet || _dispatcherModule is null
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
        _processorOptionsExplicitlySet = true;

        return this;
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
    ///     <see cref="ProcessorOptions" /> resolves the dispatcher recommendation after configuration unless the
    ///     application supplied processor options. This makes the result independent of registration order.
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
