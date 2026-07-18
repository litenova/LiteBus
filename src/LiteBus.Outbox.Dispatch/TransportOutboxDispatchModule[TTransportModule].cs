using System;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch;

/// <summary>
///     Registers <see cref="TransportOutboxDispatcher" /> against a required transport module.
/// </summary>
/// <typeparam name="TTransportModule">The transport module that must build before the dispatcher.</typeparam>
public sealed class TransportOutboxDispatchModule<TTransportModule> :
    IOutboxDispatcherModule,
    IRequires<TTransportModule>
    where TTransportModule : class, IModule
{
    /// <inheritdoc />
    public ProcessorHookFailurePolicy DefaultHookFailurePolicy =>
        ProcessorHookFailurePolicy.CompleteDespiteHookFailure;

    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly TransportOutboxDispatcherOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportOutboxDispatchModule{TTransportModule}" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    public TransportOutboxDispatchModule(TransportOutboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportOutboxDispatcherOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(TransportOutboxDispatcher)));
    }
}
