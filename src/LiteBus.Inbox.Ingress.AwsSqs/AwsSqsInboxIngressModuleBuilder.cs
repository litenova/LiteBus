using System;

namespace LiteBus.Inbox.Ingress.AwsSqs;

/// <summary>
///     Configures services registered by <see cref="AwsSqsInboxIngressModule" />.
/// </summary>
public sealed class AwsSqsInboxIngressModuleBuilder
{
    /// <summary>
    ///     Gets the ingress options that will be registered for the SQS consumer.
    /// </summary>
    public AwsSqsInboxIngressOptions Options { get; private set; } = null!;

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
    ///     Disables registration of the SQS ingress consumer background service.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AwsSqsInboxIngressModuleBuilder DisableIngressConsumer()
    {
        EnableIngressConsumer = false;
        return this;
    }

    /// <summary>
    ///     Uses an existing <see cref="LiteBus.Transport.AwsSqs.AwsSqsTransportModule" /> in the module graph instead of
    ///     declaring a child module.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AwsSqsInboxIngressModuleBuilder UseRegisteredTransport()
    {
        UseRegisteredTransportOnly = true;
        return this;
    }

    /// <summary>
    ///     Replaces the AWS SQS inbox ingress options.
    /// </summary>
    /// <param name="options">The connection and queue settings.</param>
    /// <returns>The current builder.</returns>
    public AwsSqsInboxIngressModuleBuilder UseOptions(AwsSqsInboxIngressOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }
}
