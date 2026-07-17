using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Runtime.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Registers saga services used by the inbox processor hook.
/// </summary>
public sealed class SagaModule : ICompositeModule
{
    /// <summary>
    ///     The saga configuration callback supplied at registration time.
    /// </summary>
    private readonly Action<SagaModuleBuilder> _configure;

    /// <summary>
    ///     The builder populated while the module graph declares children.
    /// </summary>
    private SagaModuleBuilder? _builder;

    /// <inheritdoc />
    public CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ChildrenFirst;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaModule" /> class.
    /// </summary>
    /// <param name="configure">The saga configuration callback.</param>
    public SagaModule(Action<SagaModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(registerChild);

        _builder = new SagaModuleBuilder();
        _configure(_builder);
        registerChild(_builder.CollectStorageModule());
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (_builder is null)
        {
            throw new Runtime.Abstractions.Exceptions.LiteBusConfigurationException(
                "SagaModule.Build was called without a prior DeclareChildren call. Register the module through IModuleRegistry.");
        }

        var registry = _builder.CollectRegistry();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaStateTypeRegistry),
            registry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(SagaExecutionContext),
            new SagaExecutionContext()));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaContext),
            static services => (ISagaContext) services.GetService(typeof(SagaExecutionContext))!,
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IProcessorEnvelopeHook),
            typeof(SagaProcessorHook),
            InstanceLifetime.Singleton));
    }
}
