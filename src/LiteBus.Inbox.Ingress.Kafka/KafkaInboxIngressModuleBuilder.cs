using System;
using LiteBus.Inbox.Ingress;

namespace LiteBus.Inbox.Ingress.Kafka;

/// <summary>
///     Configures services registered by <see cref="KafkaInboxIngressModule" />.
/// </summary>
public sealed class KafkaInboxIngressModuleBuilder
{
    /// <summary>
    ///     Gets the ingress options that will be registered for the Kafka consumer.
    /// </summary>
    public KafkaInboxIngressOptions Options { get; private set; } = null!;

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
    ///     Disables registration of the Kafka ingress consumer background service.
    /// </summary>
    /// <returns>The current builder.</returns>
    public KafkaInboxIngressModuleBuilder DisableIngressConsumer()
    {
        EnableIngressConsumer = false;
        return this;
    }

    /// <summary>
    ///     Uses an existing <see cref="LiteBus.Transport.Kafka.KafkaTransportModule" /> in the module graph instead of
    ///     declaring a child module.
    /// </summary>
    /// <returns>The current builder.</returns>
    public KafkaInboxIngressModuleBuilder UseRegisteredTransport()
    {
        UseRegisteredTransportOnly = true;
        return this;
    }

    /// <summary>
    ///     Replaces the Kafka inbox ingress host options.
    /// </summary>
    /// <param name="configure">The action that configures host options.</param>
    /// <returns>The current builder.</returns>
    public KafkaInboxIngressModuleBuilder ConfigureHost(Action<TransportInboxIngressHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(HostOptions);
        return this;
    }

    /// <summary>
    ///     Replaces the Kafka inbox ingress options.
    /// </summary>
    /// <param name="options">The connection and topic settings.</param>
    /// <returns>The current builder.</returns>
    public KafkaInboxIngressModuleBuilder UseOptions(KafkaInboxIngressOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }
}
