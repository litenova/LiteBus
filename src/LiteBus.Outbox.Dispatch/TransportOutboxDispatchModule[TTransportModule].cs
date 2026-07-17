using System;
using System.Linq;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.Dispatch;

/// <summary>
///     Registers <see cref="TransportOutboxDispatcher" /> against a required transport module.
/// </summary>
/// <typeparam name="TTransportModule">The transport module that must build before the dispatcher.</typeparam>
public sealed class TransportOutboxDispatchModule<TTransportModule> :
    IOutboxDispatcherModule,
    ICompositeModule,
    IRequires<TTransportModule>
    where TTransportModule : class, IModule
{
    /// <inheritdoc />
    public CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ChildrenFirst;

    /// <inheritdoc />
    public ProcessorHookFailurePolicy DefaultHookFailurePolicy =>
        ProcessorHookFailurePolicy.CompleteDespiteHookFailure;

    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly TransportOutboxDispatcherOptions _options;

    /// <summary>
    ///     Gets the optional transport child owned by this dispatcher module.
    /// </summary>
    private readonly TTransportModule? _transportModule;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportOutboxDispatchModule{TTransportModule}" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    /// <param name="transportModule">
    ///     The transport child owned by this module, or <see langword="null" /> when another module owns it.
    /// </param>
    public TransportOutboxDispatchModule(
        TransportOutboxDispatcherOptions options,
        TTransportModule? transportModule = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _transportModule = transportModule;
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(registerChild);

        if (_transportModule is not null)
        {
            registerChild(_transportModule);
        }
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        OutboxModuleRegistrationGuard.EnsureCoreRegistered(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IOutboxDispatcher)))
        {
            throw new LiteBusConfigurationException(
                "An IOutboxDispatcher is already registered. Register only one outbox dispatcher implementation.");
        }

        if (!configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageTransport)))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(TransportOutboxDispatchModule<TTransportModule>)} requires {nameof(IMessageTransport)} to be registered. " +
                "Register the required transport module in the module graph.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportOutboxDispatcherOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(TransportOutboxDispatcher)));
    }
}
