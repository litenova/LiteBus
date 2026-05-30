using System;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Configures services registered by <see cref="AmqpInboxIngressModule" />.
/// </summary>
public sealed class AmqpInboxIngressModuleBuilder
{
    /// <summary>
    ///     Gets the ingress options that will be registered for the AMQP consumer.
    /// </summary>
    public AmqpInboxIngressOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets the options for the ingress background loop.
    /// </summary>
    public AmqpInboxIngressHostOptions HostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="AmqpInboxIngressBackgroundWork" /> is registered.
    /// </summary>
    public bool RegisterBackgroundWork { get; private set; } = true;

    /// <summary>
    ///     Disables registration of the AMQP ingress background loop.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AmqpInboxIngressModuleBuilder DisableBackgroundWork()
    {
        RegisterBackgroundWork = false;
        return this;
    }

    /// <summary>
    ///     Replaces the AMQP inbox ingress options.
    /// </summary>
    /// <param name="options">The broker connection and queue settings.</param>
    /// <returns>The current builder.</returns>
    public AmqpInboxIngressModuleBuilder UseOptions(AmqpInboxIngressOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}
