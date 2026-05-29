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
            typeof(IMessageContractRegistrar),
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

        if (moduleBuilder.IsProcessorHostEnabled)
        {
            RegisterProcessorHost(configuration, moduleBuilder);
        }
    }

    /// <summary>
    ///     Registers DI-neutral processor host services and stores hosting metadata for integration modules.
    /// </summary>
    /// <param name="configuration">The module configuration used for dependency registration.</param>
    /// <param name="moduleBuilder">The inbox module builder that contains host options.</param>
    private static void RegisterProcessorHost(IModuleConfiguration configuration, CommandInboxModuleBuilder moduleBuilder)
    {
        var hostOptions = moduleBuilder.HostOptions;
        var hostState = new Hosting.CommandInboxProcessorHostState();

        configuration.SetContext(new CommandInboxProcessorHostRegistration
        {
            HostOptions = hostOptions
        });

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(CommandInboxProcessorHostOptions),
            hostOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Hosting.CommandInboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandInboxProcessorHostState),
            hostState));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandInboxProcessorHost),
            typeof(Hosting.CommandInboxProcessorHost)));
    }
}