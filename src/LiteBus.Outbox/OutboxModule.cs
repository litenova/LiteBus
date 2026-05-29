using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Module for configuring event publication.
/// </summary>
public sealed class OutboxModule : IModule
{
    private readonly Action<OutboxModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public OutboxModule(Action<OutboxModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        var moduleBuilder = new OutboxModuleBuilder(contractRegistry);

        _builder(moduleBuilder);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            contractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistrar),
            contractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxProcessorOptions),
            moduleBuilder.ProcessorOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxWriter),
            typeof(OutboxWriter)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IIntegrationOutbox),
            typeof(IntegrationOutbox)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessor),
            typeof(OutboxProcessor)));

        if (moduleBuilder.RegisterLiteBusEventDispatcher)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IOutboxDispatcher),
                typeof(LiteBusEventOutboxDispatcher)));
        }

        if (moduleBuilder.IsProcessorHostEnabled)
        {
            RegisterProcessorHost(configuration, moduleBuilder);
        }
    }

    /// <summary>
    ///     Registers DI-neutral processor host services and stores hosting metadata for integration modules.
    /// </summary>
    /// <param name="configuration">The module configuration used for dependency registration.</param>
    /// <param name="moduleBuilder">The outbox module builder that contains host options.</param>
    private static void RegisterProcessorHost(IModuleConfiguration configuration, OutboxModuleBuilder moduleBuilder)
    {
        var hostOptions = moduleBuilder.HostOptions;
        var hostState = new Hosting.OutboxProcessorHostState();

        configuration.SetContext(new OutboxProcessorHostRegistration
        {
            HostOptions = hostOptions
        });

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(OutboxProcessorHostOptions),
            hostOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Hosting.OutboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessorHost),
            typeof(Hosting.OutboxProcessorHost)));
    }
}