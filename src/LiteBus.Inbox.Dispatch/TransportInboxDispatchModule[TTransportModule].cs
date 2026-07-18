using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Dispatch;

/// <summary>
///     Registers <see cref="TransportInboxDispatcher" /> against a required transport module.
/// </summary>
/// <typeparam name="TTransportModule">The transport module that must build before the dispatcher.</typeparam>
public sealed class TransportInboxDispatchModule<TTransportModule> :
    IInboxDispatcherModule,
    IRequires<TTransportModule>
    where TTransportModule : class, IModule
{
    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly TransportInboxDispatcherOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportInboxDispatchModule{TTransportModule}" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    public TransportInboxDispatchModule(TransportInboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportInboxDispatcherOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDispatcher),
            typeof(TransportInboxDispatcher)));
    }
}
