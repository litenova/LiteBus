using System;

namespace LiteBus.Inbox.Ingress.InMemory;

/// <summary>
///     Configures services registered by <see cref="InMemoryInboxIngressModule" />.
/// </summary>
public sealed class InMemoryInboxIngressModuleBuilder
{
    /// <summary>
    ///     Gets the ingress options that will be registered for the in-memory consumer.
    /// </summary>
    public InMemoryInboxIngressOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets the options for the ingress background loop.
    /// </summary>
    public TransportInboxIngressHostOptions HostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="TransportInboxIngressConsumer" /> is registered.
    /// </summary>
    public bool EnableIngressConsumer { get; private set; } = true;

    /// <summary>
    ///     Disables registration of the in-memory ingress consumer background service.
    /// </summary>
    /// <returns>The current builder.</returns>
    public InMemoryInboxIngressModuleBuilder DisableIngressConsumer()
    {
        EnableIngressConsumer = false;
        return this;
    }

    /// <summary>
    ///     Replaces the in-memory inbox ingress options.
    /// </summary>
    /// <param name="options">The queue settings.</param>
    /// <returns>The current builder.</returns>
    public InMemoryInboxIngressModuleBuilder UseOptions(InMemoryInboxIngressOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }
}
