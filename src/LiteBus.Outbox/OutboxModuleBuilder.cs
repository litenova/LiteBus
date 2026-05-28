using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Configures services owned by the outbox module.
/// </summary>
/// <remarks>
///     Use this builder from `AddOutboxModule`. Register every durable event contract through <see cref="Contracts" />,
///     choose processor defaults through <see cref="UseProcessorOptions" />, and opt in to local LiteBus event dispatch
///     through <see cref="UseLiteBusEventDispatcher" /> when the outbox should replay events into in-process handlers.
///     Broker dispatchers can register their own <see cref="IOutboxDispatcher" /> instead.
/// </remarks>
public sealed class OutboxModuleBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxModuleBuilder" /> class.
    /// </summary>
    /// <param name="contracts">The durable message contract registrar.</param>
    public OutboxModuleBuilder(IMessageContractRegistrar contracts)
    {
        Contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <summary>
    ///     Gets the durable message contract registrar shared with the messaging module.
    /// </summary>
    public IMessageContractRegistrar Contracts { get; }

    /// <summary>
    ///     Gets the outbox processor options that will be registered for <see cref="IOutboxProcessor" />.
    /// </summary>
    public OutboxProcessorOptions ProcessorOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value that indicates whether the in-process LiteBus event dispatcher should be registered.
    /// </summary>
    public bool RegisterLiteBusEventDispatcher { get; private set; }

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

    /// <summary>
    ///     Registers the dispatcher that publishes outbox messages through <see cref="Events.Abstractions.IEventPublisher" />.
    ///     Do not call this when another module registers an external broker dispatcher as <see cref="IOutboxDispatcher" />.
    /// </summary>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseLiteBusEventDispatcher()
    {
        RegisterLiteBusEventDispatcher = true;
        return this;
    }
}