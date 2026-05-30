using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Module for configuring inbox acceptance and processing orchestration.
/// </summary>
public sealed class InboxModule : IModule
{
    /// <summary>
    ///     Gets the module builder callback invoked during <see cref="Build" />.
    /// </summary>
    private readonly Action<InboxModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public InboxModule(Action<InboxModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        var moduleBuilder = new InboxModuleBuilder(contractRegistry);

        _builder(moduleBuilder);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            contractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InboxProcessorOptions),
            moduleBuilder.ProcessorOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInbox),
            typeof(InboxWriter)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.IInboxProcessor),
            typeof(InboxProcessor)));

        if (moduleBuilder.RegisterProcessorBackgroundWork)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxProcessorHostOptions),
                moduleBuilder.ProcessorHostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(InboxProcessorBackgroundWork),
                typeof(InboxProcessorBackgroundWork)));

            configuration.DependencyRegistry.RegisterBackgroundWork(typeof(InboxProcessorBackgroundWork));
        }
    }
}
