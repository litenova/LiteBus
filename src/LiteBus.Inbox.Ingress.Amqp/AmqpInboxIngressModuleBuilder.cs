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
    public TransportInboxIngressHostOptions HostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="TransportInboxIngressConsumer" /> is registered.
    /// </summary>
    public bool EnableIngressConsumer { get; private set; } = true;

    /// <summary>
    ///     Gets a value indicating whether ingress should use a transport module registered elsewhere in the graph.
    /// </summary>
    internal bool UseRegisteredTransportOnly { get; private set; }

    /// <summary>
    ///     Disables registration of the AMQP ingress consumer background service.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AmqpInboxIngressModuleBuilder DisableIngressConsumer()
    {
        EnableIngressConsumer = false;
        return this;
    }

    /// <summary>
    ///     Uses an existing <see cref="LiteBus.Transport.Amqp.AmqpTransportModule" /> in the module graph instead of
    ///     declaring a child module.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AmqpInboxIngressModuleBuilder UseRegisteredTransport()
    {
        UseRegisteredTransportOnly = true;
        return this;
    }

    /// <summary>
    ///     Replaces the AMQP inbox ingress options.
    /// </summary>
    /// <param name="options">The broker connection and queue settings.</param>
    /// <returns>The current builder.</returns>
    public AmqpInboxIngressModuleBuilder UseOptions(AmqpInboxIngressOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }
}
