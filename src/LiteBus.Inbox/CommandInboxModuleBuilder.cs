using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Configures services owned by the command inbox module.
/// </summary>
/// <remarks>
///     Use this builder from `AddCommandInboxModule`. Register every inbox command contract through
///     <see cref="Contracts" /> and optionally replace processor defaults through <see cref="UseProcessorOptions" />.
///     Store registration is supplied by a storage module such as PostgreSQL or by application DI registration.
/// </remarks>
public sealed class CommandInboxModuleBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxModuleBuilder" /> class.
    /// </summary>
    /// <param name="contracts">The message contract registrar.</param>
    public CommandInboxModuleBuilder(IMessageContractRegistrar contracts)
    {
        Contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <summary>
    ///     Gets the message contract registrar shared with the messaging module.
    /// </summary>
    public IMessageContractRegistrar Contracts { get; }

    /// <summary>
    ///     Gets the command inbox processor options that will be registered for `ICommandInboxProcessor`.
    /// </summary>
    public CommandInboxProcessorOptions ProcessorOptions { get; private set; } = new();

    /// <summary>
    ///     Gets the command inbox processor host options that will be registered when hosting is enabled.
    /// </summary>
    public CommandInboxProcessorHostOptions HostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value that indicates whether processor hosting was requested for this module registration.
    /// </summary>
    public bool IsProcessorHostEnabled { get; private set; }

    /// <summary>
    ///     Replaces the command inbox processor options.
    /// </summary>
    /// <param name="options">The batch, lease, owner, and retry options used by the processor.</param>
    /// <returns>The current builder.</returns>
    public CommandInboxModuleBuilder UseProcessorOptions(CommandInboxProcessorOptions options)
    {
        ProcessorOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    ///     Enables the command inbox processor host and optionally configures its loop behavior.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Register <see cref="ModuleRegistryHostingExtensions.AddCommandInboxProcessorHosting" /> to add an
    ///     <see cref="Microsoft.Extensions.Hosting.IHostedService" /> wrapper.
    /// </remarks>
    public CommandInboxModuleBuilder UseProcessorHost(Action<CommandInboxProcessorHostOptions>? configure = null)
    {
        IsProcessorHostEnabled = true;
        configure?.Invoke(HostOptions);
        return this;
    }
}