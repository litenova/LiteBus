using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Saga.Abstractions;
using Marten;

namespace LiteBus.Saga.Storage.Marten;

/// <summary>
///     Module for registering the Marten saga store.
/// </summary>
/// <remarks>
///     Declared as the storage child of <see cref="Saga.SagaModule" /> through
///     <see cref="SagaModuleBuilderMartenExtensions.UseMarten(Saga.SagaModuleBuilder, Action{MartenSagaStoreOptions})" />, so the store is composed inside
///     the saga composition and cannot be registered without the axis it persists. The application
///     registers Marten itself with <c>AddMarten</c>; this module only resolves the application-owned
///     <see cref="IDocumentStore" /> and the messaging <see cref="TimeProvider" />.
/// </remarks>
public sealed class MartenSagaModule : ISagaStorageModule
{
    /// <summary>
    ///     Gets the saga store configuration captured at registration time.
    /// </summary>
    private readonly MartenSagaStoreOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenSagaModule" /> class.
    /// </summary>
    /// <param name="options">The saga store configuration.</param>
    public MartenSagaModule(MartenSagaStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = _options;

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaStore),
            serviceProvider => new MartenSagaStore(
                (IDocumentStore)(serviceProvider.GetService(typeof(IDocumentStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentStore))),
                (IMessageSerializer)(serviceProvider.GetService(typeof(IMessageSerializer)) ?? throw new LiteBusDependencyResolutionException(typeof(IMessageSerializer))),
                options,
                (TimeProvider?)serviceProvider.GetService(typeof(TimeProvider))),
            InstanceLifetime.Singleton));
    }
}
