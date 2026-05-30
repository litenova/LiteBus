using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Configures services owned by the outbox module.
/// </summary>
/// <remarks>
///     Use this builder from `AddOutboxModule`. Register every outbox message contract through
///     <see cref="Contracts" /> and optionally replace processor defaults through <see cref="UseProcessorOptions" />.
///     Store and dispatcher registration are supplied by storage and dispatch modules, or by application DI registration.
///     Enable background processing through <see cref="UseProcessorBackgroundWork" />.
/// </remarks>
public sealed class OutboxModuleBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxModuleBuilder" /> class.
    /// </summary>
    /// <param name="contracts">The message contract registry.</param>
    public OutboxModuleBuilder(IMessageContractRegistry contracts)
    {
        Contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <summary>
    ///     Gets the message contract registry shared with the messaging module.
    /// </summary>
    public IMessageContractRegistry Contracts { get; }

    /// <summary>
    ///     Gets the outbox processor options that will be registered for <see cref="IOutboxProcessor" />.
    /// </summary>
    public OutboxProcessorOptions ProcessorOptions { get; private set; } = new();

    /// <summary>
    ///     Gets the options for the optional outbox processor background loop.
    /// </summary>
    public OutboxProcessorHostOptions ProcessorHostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="OutboxProcessorBackgroundWork" /> is registered.
    /// </summary>
    public bool RegisterProcessorBackgroundWork { get; private set; }

    /// <summary>
    ///     Registers the outbox processor background loop for the generic host.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseProcessorBackgroundWork(Action<OutboxProcessorHostOptions>? configure = null)
    {
        RegisterProcessorBackgroundWork = true;
        configure?.Invoke(ProcessorHostOptions);
        return this;
    }

    /// <summary>
    ///     Replaces the outbox processor options.
    /// </summary>
    /// <param name="options">The batch, lease, owner, and retry options used by the processor.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseProcessorOptions(OutboxProcessorOptions options)
    {
        ProcessorOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}
