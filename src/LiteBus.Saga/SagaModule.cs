using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Registers saga services used by the inbox processor hook.
/// </summary>
public sealed class SagaModule : IModule
{
    /// <summary>
    ///     The saga configuration callback supplied at registration time.
    /// </summary>
    private readonly Action<SagaModuleBuilder> _configure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaModule" /> class.
    /// </summary>
    /// <param name="configure">The saga configuration callback.</param>
    public SagaModule(Action<SagaModuleBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = new SagaModuleBuilder();
        _configure(builder);
        var registry = builder.CollectRegistry();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaStateTypeRegistry),
            registry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(SagaExecutionContext),
            new SagaExecutionContext()));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaContext),
            static services => (ISagaContext)services.GetService(typeof(SagaExecutionContext))!,
            InstanceLifetime.Singleton));

        if (!configuration.TryGetContext<SagaStoreRegisteredMarker>(out _))
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(ISagaStore),
                typeof(InMemorySagaStore),
                InstanceLifetime.Singleton));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxProcessorEnvelopeHook),
            typeof(SagaProcessorHook),
            InstanceLifetime.Singleton));
    }
}
