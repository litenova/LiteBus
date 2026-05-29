using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Module for configuring explicit command scheduling.
/// </summary>
public sealed class CommandInboxModule : IModule
{
    /// <summary>
    ///     Gets the module builder callback invoked during <see cref="Build" />.
    /// </summary>
    private readonly Action<CommandInboxModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public CommandInboxModule(Action<CommandInboxModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        var moduleBuilder = new CommandInboxModuleBuilder(contractRegistry);

        _builder(moduleBuilder);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            contractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(CommandInboxProcessorOptions),
            moduleBuilder.ProcessorOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandScheduler),
            typeof(CommandScheduler)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.ICommandInboxProcessor),
            typeof(CommandInboxProcessor)));
    }
}
