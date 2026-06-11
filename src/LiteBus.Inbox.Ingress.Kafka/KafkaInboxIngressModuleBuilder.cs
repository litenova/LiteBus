using System;

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
    ///     Disables registration of the Kafka ingress consumer background service.
    /// </summary>
    /// <returns>The current builder.</returns>
    public KafkaInboxIngressModuleBuilder DisableIngressConsumer()
    {
        EnableIngressConsumer = false;
        return this;
    }

    /// <summary>
    ///     Replaces the Kafka inbox ingress options.
    /// </summary>
    /// <param name="options">The connection and topic settings.</param>
    /// <returns>The current builder.</returns>
    public KafkaInboxIngressModuleBuilder UseOptions(KafkaInboxIngressOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}