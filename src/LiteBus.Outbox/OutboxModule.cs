using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Module for configuring durable outbox orchestration.
/// </summary>
public sealed class OutboxModule : IModule
{
    /// <summary>
    ///     Gets the module builder callback invoked during <see cref="Build" />.
    /// </summary>
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
            typeof(OutboxProcessorOptions),
            moduleBuilder.ProcessorOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutbox),
            typeof(OutboxWriter)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessor),
            typeof(OutboxProcessor)));

        if (moduleBuilder.RegisterProcessorBackgroundWork)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxProcessorHostOptions),
                moduleBuilder.ProcessorHostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(OutboxProcessorBackgroundWork),
                typeof(OutboxProcessorBackgroundWork)));

            configuration.DependencyRegistry.RegisterBackgroundWork(typeof(OutboxProcessorBackgroundWork));
        }
    }
}
