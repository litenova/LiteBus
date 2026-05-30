using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Configures services owned by the inbox module.
/// </summary>
/// <remarks>
///     Use this builder from `AddInboxModule`. Register every inbox message contract through
///     <see cref="Contracts" /> and optionally replace processor defaults through <see cref="UseProcessorOptions" />.
///     Store registration is supplied by a storage module such as PostgreSQL or by application DI registration.
///     Dispatch registration is supplied by a dispatch module or by application DI registration.
///     Enable background processing through <see cref="UseProcessorBackgroundWork" />.
/// </remarks>
public sealed class InboxModuleBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxModuleBuilder" /> class.
    /// </summary>
    /// <param name="contracts">The message contract registrar.</param>
    public InboxModuleBuilder(IMessageContractRegistry contracts)
    {
        Contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <summary>
    ///     Gets the message contract registry shared with the messaging module.
    /// </summary>
    public IMessageContractRegistry Contracts { get; }

    /// <summary>
    ///     Gets the inbox processor options that will be registered for <see cref="IInboxProcessor" />.
    /// </summary>
    public InboxProcessorOptions ProcessorOptions { get; private set; } = new();

    /// <summary>
    ///     Gets the options for the optional inbox processor background loop.
    /// </summary>
    public InboxProcessorHostOptions ProcessorHostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="InboxProcessorBackgroundWork" /> is registered.
    /// </summary>
    public bool RegisterProcessorBackgroundWork { get; private set; }

    /// <summary>
    ///     Registers the inbox processor background loop for the generic host.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder UseProcessorBackgroundWork(Action<InboxProcessorHostOptions>? configure = null)
    {
        RegisterProcessorBackgroundWork = true;
        configure?.Invoke(ProcessorHostOptions);
        return this;
    }

    /// <summary>
    ///     Replaces the inbox processor options.
    /// </summary>
    /// <param name="options">The batch, lease, owner, and retry options used by the processor.</param>
    /// <returns>The current builder.</returns>
    public InboxModuleBuilder UseProcessorOptions(InboxProcessorOptions options)
    {
        ProcessorOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}
